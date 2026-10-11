using System.Security.Cryptography;

using BackupZCrypt.Application.FileSystem;
using BackupZCrypt.Domain.Archive;

namespace BackupZCrypt.Application.Engine;

/// <summary>
/// Reads the chunks of one opened backup back into the files they make up, authenticating every
/// chunk and checking each file against its manifest entry.
/// </summary>
/// <param name="fileOperationsService">The service used to read chunk files.</param>
/// <param name="chunksDirectory">The directory holding the chunk files.</param>
/// <param name="cipher">The archive's chunk sealer and naming sub-key.</param>
internal sealed class ChunkReader(
    IFileOperationsService fileOperationsService,
    string chunksDirectory,
    ChunkCipherSet cipher
)
{
    /// <summary>
    /// Opens every chunk of a file into <paramref name="destination"/>, then checks the reassembled
    /// size and SHA-256 hash against the manifest.
    /// </summary>
    /// <remarks>
    /// A stored chunk whose length is impossible for the size the manifest declares is refused before
    /// it is read, and <see cref="ChunkSealer.OpenAsync"/> authenticates each chunk before any of it
    /// reaches <paramref name="destination"/>. Verification passes <see cref="Stream.Null"/> as the
    /// destination to discard the plaintext. Each chunk's decoded hash and stored bytes are zeroed
    /// once the chunk has been written, and the expected and computed file hashes are zeroed after
    /// they are compared.
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
            byte[]? storedChunk = null;
            string? chunkFilePath = null;

            try
            {
                chunkHash = ManifestEntries.DecodeBase64FixedLength(
                    chunkRef.Hash,
                    SHA256.HashSizeInBytes,
                    "Invalid chunk hash."
                );
                chunkFilePath = BackupLayout.ChunkFilePath(chunksDirectory, cipher.NamingKey, chunkHash);

                if (!fileOperationsService.FileExists(chunkFilePath))
                {
                    throw new MissingChunkException();
                }

                var storedSize = fileOperationsService.GetFileSize(chunkFilePath);

                if (!cipher.Sealer.IsPlausibleStoredSize(storedSize, chunkRef.Size))
                {
                    throw new InvalidDataException("Stored chunk size is invalid.");
                }

                storedChunk = await fileOperationsService
                    .ReadAllBytesBoundedAsync(
                        chunkFilePath,
                        cipher.Sealer.MaximumStoredSize(chunkRef.Size),
                        cancellationToken
                    )
                    .ConfigureAwait(false);

                var openedBytes = await cipher.Sealer
                    .OpenAsync(
                        storedChunk,
                        chunkHash,
                        chunkRef.Size,
                        destination,
                        fileHasher,
                        cancellationToken
                    )
                    .ConfigureAwait(false);

                processedBytes = checked(processedBytes + openedBytes);
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

                if (storedChunk is not null)
                {
                    CryptographicOperations.ZeroMemory(storedChunk);
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
}
