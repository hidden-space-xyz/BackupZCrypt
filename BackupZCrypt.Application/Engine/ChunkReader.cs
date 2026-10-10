using System.Buffers;
using System.Security.Cryptography;

using BackupZCrypt.Application.Utilities.Helpers;
using BackupZCrypt.Domain.Archive;
using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Services.Interfaces;
using BackupZCrypt.Domain.Strategies.Interfaces;

namespace BackupZCrypt.Application.Engine;

/// <summary>
/// Reads the chunks of one opened backup back into the files they make up, authenticating every
/// chunk and checking each file against its manifest entry.
/// </summary>
/// <param name="fileOperationsService">The service used to read chunk files.</param>
/// <param name="chunksDirectory">The directory holding the chunk files.</param>
/// <param name="encryptionKey">The chunk encryption sub-key.</param>
/// <param name="nonceKey">The sub-key each chunk's nonce is derived from.</param>
/// <param name="namingKey">The sub-key each chunk's on-disk file name is derived from.</param>
/// <param name="encryptionStrategy">The strategy used to decrypt chunks.</param>
/// <param name="compressionStrategy">The decompression strategy, or <see langword="null"/> if chunks are uncompressed.</param>
internal sealed class ChunkReader(
    IFileOperationsService fileOperationsService,
    string chunksDirectory,
    byte[] encryptionKey,
    byte[] nonceKey,
    byte[] namingKey,
    IEncryptionAlgorithmStrategy encryptionStrategy,
    ICompressionStrategy? compressionStrategy
)
{
    /// <summary>
    /// Decrypts and authenticates every chunk of a file into <paramref name="destination"/>, then
    /// checks the reassembled size and SHA-256 hash against the manifest.
    /// </summary>
    /// <remarks>
    /// Each chunk is authenticated with its content hash concatenated with its nonce as associated
    /// data, so a tampered chunk fails before any of it reaches <paramref name="destination"/>, and
    /// decompression is bounded by the size the manifest declares for the chunk so it cannot expand
    /// past it. Verification passes <see cref="Stream.Null"/> as the destination to discard the
    /// plaintext. Each chunk's decoded hash, nonce, associated data, ciphertext, and plaintext are
    /// zeroed once the chunk has been written, and the expected and computed file hashes are zeroed
    /// after they are compared.
    /// </remarks>
    /// <param name="fileEntry">The manifest entry describing the file and its chunks.</param>
    /// <param name="destination">The stream the reconstructed plaintext is written to.</param>
    /// <param name="tracker">The progress tracker that is told about every chunk processed.</param>
    /// <param name="onDamagedChunk">Told about a chunk file whose content fails authentication, if not <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when every chunk has been verified and written to <paramref name="destination"/>.</returns>
    /// <exception cref="MissingChunkException">A chunk file is missing.</exception>
    /// <exception cref="InvalidDataException">The entry path is malformed or a chunk decompresses beyond the declared size.</exception>
    /// <exception cref="CryptographicException">A chunk fails authentication or the size or hash does not match the manifest.</exception>
    internal async Task ReadFileAsync(
        ChunkManifestFileEntry fileEntry,
        Stream destination,
        ProgressTracker tracker,
        Action<string>? onDamagedChunk,
        CancellationToken cancellationToken
    )
    {
        ManifestPathPolicy.ValidateRelative(fileEntry.OriginalPath);

        using var fileHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long processedBytes = 0;

        foreach (var chunkRef in fileEntry.Chunks)
        {
            byte[]? chunkHash = null;
            byte[]? nonce = null;
            byte[]? encryptedData = null;
            byte[]? associatedData = null;
            byte[]? decryptedData = null;
            string? chunkFilePath = null;

            try
            {
                chunkHash = ManifestEntries.DecodeBase64FixedLength(
                    chunkRef.Hash,
                    SHA256.HashSizeInBytes,
                    "Invalid chunk hash."
                );
                nonce = ChunkCrypto.ComputeChunkNonce(nonceKey, chunkHash);
                chunkFilePath = BackupLayout.ChunkFilePath(chunksDirectory, namingKey, chunkHash);

                if (!fileOperationsService.FileExists(chunkFilePath))
                {
                    throw new MissingChunkException();
                }

                var storedSize = fileOperationsService.GetFileSize(chunkFilePath);
                var maximumStoredSize = compressionStrategy is null
                    ? checked(chunkRef.Size + EncryptionConstants.TagSize)
                    : EngineLimits.MaximumStoredChunkSize;

                if (
                    storedSize < EncryptionConstants.TagSize
                    || storedSize > maximumStoredSize
                    || (compressionStrategy is null && storedSize != maximumStoredSize)
                )
                {
                    throw new InvalidDataException("Stored chunk size is invalid.");
                }

                encryptedData = await fileOperationsService
                    .ReadAllBytesBoundedAsync(chunkFilePath, maximumStoredSize, cancellationToken)
                    .ConfigureAwait(false);

                associatedData = ChunkCrypto.BuildChunkAssociatedData(chunkHash, nonce);
                decryptedData = encryptionStrategy.DecryptChunk(
                    encryptedData,
                    encryptionKey,
                    nonce,
                    associatedData
                );

                if (compressionStrategy is not null)
                {
                    await using MemoryStream compressedStream = new(decryptedData, writable: false);

                    await using var decompressedStream = await compressionStrategy
                        .DecompressAsync(compressedStream, cancellationToken)
                        .ConfigureAwait(false);

                    var decompressedSize = await CopyToWithHashAsync(
                            decompressedStream,
                            destination,
                            fileHasher,
                            chunkRef.Size,
                            cancellationToken
                        )
                        .ConfigureAwait(false);

                    if (decompressedSize != chunkRef.Size)
                    {
                        throw new CryptographicException(
                            "Decompressed chunk size does not match the manifest."
                        );
                    }

                    processedBytes = checked(processedBytes + decompressedSize);
                }
                else
                {
                    if (decryptedData.Length != chunkRef.Size)
                    {
                        throw new CryptographicException(
                            "Chunk size does not match the manifest."
                        );
                    }

                    fileHasher.AppendData(decryptedData);
                    await destination
                        .WriteAsync(decryptedData, cancellationToken)
                        .ConfigureAwait(false);
                    processedBytes = checked(processedBytes + decryptedData.Length);
                }

                tracker.AddBytes(chunkRef.Size);
            }
            catch (Exception exception)
                when (chunkFilePath is not null
                    && exception is CryptographicException or InvalidDataException)
            {
                onDamagedChunk?.Invoke(chunkFilePath);
                throw;
            }
            finally
            {
                if (chunkHash is not null)
                {
                    CryptographicOperations.ZeroMemory(chunkHash);
                }

                if (nonce is not null)
                {
                    CryptographicOperations.ZeroMemory(nonce);
                }

                if (associatedData is not null)
                {
                    CryptographicOperations.ZeroMemory(associatedData);
                }

                if (encryptedData is not null)
                {
                    CryptographicOperations.ZeroMemory(encryptedData);
                }

                if (decryptedData is not null)
                {
                    CryptographicOperations.ZeroMemory(decryptedData);
                }
            }
        }

        if (processedBytes != fileEntry.TotalSize)
        {
            throw new CryptographicException("File size does not match the manifest.");
        }

        var expectedFileHash = ManifestEntries.DecodeBase64FixedLength(
            fileEntry.FileHash,
            SHA256.HashSizeInBytes,
            "Invalid file hash."
        );
        var actualFileHash = fileHasher.GetHashAndReset();

        try
        {
            if (!CryptographicOperations.FixedTimeEquals(expectedFileHash, actualFileHash))
            {
                throw new CryptographicException("File hash does not match the manifest.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expectedFileHash);
            CryptographicOperations.ZeroMemory(actualFileHash);
        }
    }

    /// <summary>
    /// Copies a stream to a destination while appending the copied bytes to a running hash, stopping
    /// with an error if the copy would exceed the caller's byte budget.
    /// </summary>
    /// <remarks>
    /// The budget bounds decompression to the size the manifest declares for the chunk, so a crafted
    /// chunk cannot expand without limit. The pooled buffer is zeroed before it is returned.
    /// </remarks>
    /// <param name="source">The stream to read from.</param>
    /// <param name="destination">The stream the bytes are written to.</param>
    /// <param name="hasher">The running hash the copied bytes are appended to.</param>
    /// <param name="maxBytes">The maximum number of bytes this copy is allowed to produce.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The number of bytes copied.</returns>
    /// <exception cref="InvalidDataException">The source yields more than <paramref name="maxBytes"/> bytes.</exception>
    private static async Task<long> CopyToWithHashAsync(
        Stream source,
        Stream destination,
        IncrementalHash hasher,
        long maxBytes,
        CancellationToken cancellationToken
    )
    {
        var buffer = ArrayPool<byte>.Shared.Rent(StreamConstants.CopyBufferSize);
        long total = 0;

        try
        {
            while (true)
            {
                var read = await source
                    .ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                    .ConfigureAwait(false);

                if (read is 0)
                {
                    return total;
                }

                total += read;

                if (total > maxBytes)
                {
                    throw new InvalidDataException(
                        "Decompressed data exceeds the size declared in the manifest."
                    );
                }

                hasher.AppendData(buffer.AsSpan(0, read));
                await destination
                    .WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer.AsSpan(0, buffer.Length));
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
