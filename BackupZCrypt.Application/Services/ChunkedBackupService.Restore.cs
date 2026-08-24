using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;

using BackupZCrypt.Application.Utilities.Helpers;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Application.ValueObjects.Manifest;
using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Backup;
using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Application.Services;

internal sealed partial class ChunkedBackupService
{
    /// <summary>
    /// Restores files from a chunked backup, decrypting and reassembling chunks in parallel and
    /// verifying each restored file's size and hash against the manifest.
    /// </summary>
    /// <param name="sourcePath">The directory containing the backup chunks and manifest.</param>
    /// <param name="destinationPath">The directory into which files are reconstructed.</param>
    /// <param name="request">The backup request carrying the password used to decrypt the manifest.</param>
    /// <param name="progress">A sink that receives incremental status updates.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A result describing the restore outcome; a wrong password surfaces as a failure result.</returns>
    public async Task<Result<BackupResult>> RestoreAsync(
        string sourcePath,
        string destinationPath,
        BackupRequest request,
        IProgress<BackupStatus> progress,
        CancellationToken cancellationToken
    )
    {
        var stopwatch = Stopwatch.StartNew();

        var preamble = await manifestService
            .ReadChunkManifestPreambleAsync(sourcePath, cancellationToken)
            .ConfigureAwait(false);

        if (preamble is null)
        {
            return Result<BackupResult>.Failure(MessageCode.ManifestRequiredForDecryption);
        }

        DerivedKeySet? keys = null;

        try
        {
            keys = DeriveKeySet(request.Password, preamble.MasterSalt, preamble.KeyDerivation);

            var manifest = manifestService.DecryptChunkManifest(
                preamble,
                keys.ManifestEncryptionKey
            );

            if (manifest is null)
            {
                return Result<BackupResult>.Failure(MessageCode.InvalidPassword);
            }

            var encryptionStrategy = encryptionServiceFactory.Create(manifest.Header.EncryptionAlgorithm);

            var compressionStrategy = CreateCompressionStrategy(manifest.Header.Compression);

            var chunksDir = fileOperationsService.CombinePath(
                sourcePath,
                BackupConstants.ChunksDirectoryName
            );

            ValidateManifestEntries(manifest.Files);

            var storedChunkNonces = BuildStoredChunkNonceCache(manifest.Files);

            var totalFiles = manifest.Files.Count;
            var totalBytes = manifest.Files.Sum(static f => f.TotalSize);
            progress?.Report(new BackupStatus(0, totalFiles, 0, totalBytes, TimeSpan.Zero));

            ConcurrentBag<LocalizableMessage> errors = [];
            long processedBytes = 0;
            var processedFiles = 0;
            LocalizableMessage? fatalError = null;

            await fileOperationsService
                .CreateDirectoryAsync(destinationPath, cancellationToken)
                .ConfigureAwait(false);

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken
            );

            try
            {
                await Parallel
                    .ForEachAsync(
                        manifest.Files,
                        new ParallelOptions
                        {
                            MaxDegreeOfParallelism = MaximumParallelFileOperations,
                            CancellationToken = linkedCts.Token,
                        },
                        async (fileEntry, token) =>
                        {
                            try
                            {
                                await RestoreFileFromChunksAsync(
                                        fileEntry,
                                        chunksDir,
                                        destinationPath,
                                        keys.ChunkEncryptionKey,
                                        keys.NamingKey,
                                        encryptionStrategy,
                                        storedChunkNonces,
                                        compressionStrategy,
                                        token
                                    )
                                    .ConfigureAwait(false);

                                _ = Interlocked.Increment(ref processedFiles);
                                var currentBytes = Interlocked.Add(
                                    ref processedBytes,
                                    fileEntry.TotalSize
                                );

                                progress?.Report(
                                    new BackupStatus(
                                        Volatile.Read(ref processedFiles),
                                        totalFiles,
                                        currentBytes,
                                        totalBytes,
                                        stopwatch.Elapsed
                                    )
                                );
                            }
                            catch (CryptographicException ex)
                            {
                                errors.Add(
                                    new LocalizableMessage(
                                        MessageCode.DecryptionErrorFormat,
                                        fileEntry.OriginalPath,
                                        ex.Message
                                    )
                                );
                            }
                            catch (Exception ex)
                                when (ex is not OperationCanceledException && IsFileLevelError(ex))
                            {
                                errors.Add(
                                    new LocalizableMessage(
                                        MessageCode.DecryptionErrorFormat,
                                        fileEntry.OriginalPath,
                                        ex.Message
                                    )
                                );
                            }
                            catch (Exception ex) when (ex is not OperationCanceledException)
                            {
                                _ = Interlocked.CompareExchange(
                                    ref fatalError,
                                    new LocalizableMessage(
                                        MessageCode.UnexpectedErrorFormat,
                                        ex.Message
                                    ),
                                    null
                                );
                                await linkedCts.CancelAsync().ConfigureAwait(false);
                            }
                        }
                    )
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (fatalError is not null)
            {
                stopwatch.Stop();
                return Result<BackupResult>.Failure(fatalError);
            }

            List<LocalizableMessage> errorList = [.. errors];
            stopwatch.Stop();

            return errorList.Count > 0 && processedFiles is 0
                ? Result<BackupResult>.Failure(
                    [new LocalizableMessage(MessageCode.AllFilesFailed), .. errorList]
                )
                : Result<BackupResult>.Success(
                    new BackupResult(
                        stopwatch.Elapsed,
                        totalBytes,
                        processedFiles,
                        totalFiles,
                        errors: errorList
                    )
                );
        }
        finally
        {
            keys?.Dispose();
        }
    }

    /// <summary>
    /// Verifies the integrity of a chunked backup without writing any files. After the manifest is
    /// decrypted (a wrong password surfaces as a failure), every file is processed in parallel: its
    /// chunks are decrypted, authenticated, decompressed, and re-hashed against the manifest, with
    /// the reconstructed bytes discarded to <see cref="Stream.Null"/>. Per-file failures (missing or
    /// corrupted chunks, size or hash mismatches) are collected rather than aborting the run, so the
    /// result reports every affected file.
    /// </summary>
    /// <param name="sourcePath">The directory containing the backup chunks and manifest.</param>
    /// <param name="request">The backup request carrying the password used to decrypt the manifest.</param>
    /// <param name="progress">A sink that receives incremental status updates.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A result whose value reports how many files verified successfully and any integrity errors.</returns>
    public async Task<Result<BackupResult>> VerifyAsync(
        string sourcePath,
        BackupRequest request,
        IProgress<BackupStatus> progress,
        CancellationToken cancellationToken
    )
    {
        var stopwatch = Stopwatch.StartNew();

        var preamble = await manifestService
            .ReadChunkManifestPreambleAsync(sourcePath, cancellationToken)
            .ConfigureAwait(false);

        if (preamble is null)
        {
            return Result<BackupResult>.Failure(MessageCode.ManifestRequiredForDecryption);
        }

        DerivedKeySet? keys = null;

        try
        {
            keys = DeriveKeySet(request.Password, preamble.MasterSalt, preamble.KeyDerivation);

            var manifest = manifestService.DecryptChunkManifest(
                preamble,
                keys.ManifestEncryptionKey
            );

            if (manifest is null)
            {
                return Result<BackupResult>.Failure(MessageCode.VerifyInvalidPassword);
            }

            var encryptionStrategy = encryptionServiceFactory.Create(manifest.Header.EncryptionAlgorithm);
            var compressionStrategy = CreateCompressionStrategy(manifest.Header.Compression);

            var chunksDir = fileOperationsService.CombinePath(
                sourcePath,
                BackupConstants.ChunksDirectoryName
            );

            ValidateManifestEntries(manifest.Files);

            var storedChunkNonces = BuildStoredChunkNonceCache(manifest.Files);

            var totalFiles = manifest.Files.Count;
            var totalBytes = manifest.Files.Sum(static f => f.TotalSize);
            progress?.Report(new BackupStatus(0, totalFiles, 0, totalBytes, TimeSpan.Zero));

            ConcurrentBag<LocalizableMessage> errors = [];
            long processedBytes = 0;
            var processedFiles = 0;

            await Parallel
                .ForEachAsync(
                    manifest.Files,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = MaximumParallelFileOperations,
                        CancellationToken = cancellationToken,
                    },
                    async (fileEntry, token) =>
                    {
                        try
                        {
                            await VerifyFileChunksAsync(
                                    fileEntry,
                                    chunksDir,
                                    keys.ChunkEncryptionKey,
                                    keys.NamingKey,
                                    encryptionStrategy,
                                    storedChunkNonces,
                                    compressionStrategy,
                                    Stream.Null,
                                    token
                                )
                                .ConfigureAwait(false);

                            _ = Interlocked.Increment(ref processedFiles);
                            var currentBytes = Interlocked.Add(
                                ref processedBytes,
                                fileEntry.TotalSize
                            );

                            progress?.Report(
                                new BackupStatus(
                                    Volatile.Read(ref processedFiles),
                                    totalFiles,
                                    currentBytes,
                                    totalBytes,
                                    stopwatch.Elapsed
                                )
                            );
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            errors.Add(
                                new LocalizableMessage(
                                    MessageCode.IntegrityErrorFormat,
                                    fileEntry.OriginalPath,
                                    ex.Message
                                )
                            );
                        }
                    }
                )
                .ConfigureAwait(false);

            List<LocalizableMessage> errorList = [.. errors];
            stopwatch.Stop();

            return Result<BackupResult>.Success(
                new BackupResult(
                    stopwatch.Elapsed,
                    totalBytes,
                    processedFiles,
                    totalFiles,
                    errors: errorList
                )
            );
        }
        finally
        {
            keys?.Dispose();
        }
    }

    /// <summary>
    /// Reconstructs one backed-up file on disk, creating its parent directory and writing the
    /// decrypted chunks to a path confined to the restore root.
    /// </summary>
    /// <param name="fileEntry">The manifest entry describing the file and its chunks.</param>
    /// <param name="chunksDir">The directory the encrypted chunk files are read from.</param>
    /// <param name="destinationPath">The restore root the file is written beneath.</param>
    /// <param name="encryptionKey">The chunk encryption sub-key.</param>
    /// <param name="namingKey">The sub-key each chunk's on-disk file name is derived from.</param>
    /// <param name="encryptionStrategy">The strategy used to decrypt chunks.</param>
    /// <param name="storedChunkNonces">The cache resolving a chunk hash to the nonce its stored ciphertext authenticates under.</param>
    /// <param name="compressionStrategy">The decompression strategy, or <see langword="null"/> if chunks are uncompressed.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the file has been reconstructed and checked against the manifest.</returns>
    /// <exception cref="InvalidDataException">The manifest entry path is malformed or escapes the restore root.</exception>
    private async Task RestoreFileFromChunksAsync(
        ChunkManifestFileEntry fileEntry,
        string chunksDir,
        string destinationPath,
        byte[] encryptionKey,
        byte[] namingKey,
        IEncryptionAlgorithmStrategy encryptionStrategy,
        ConcurrentDictionary<string, Lazy<Task<string>>> storedChunkNonces,
        ICompressionStrategy? compressionStrategy,
        CancellationToken cancellationToken
    )
    {
        var destFilePath = ManifestPathPolicy.ResolveSafeDestination(destinationPath, fileEntry.OriginalPath);
        var destDir = fileOperationsService.GetDirectoryName(destFilePath);

        if (!string.IsNullOrEmpty(destDir))
        {
            ManifestPathPolicy.EnsureNoReparsePointDescendants(fileOperationsService, destinationPath, destDir);
            await fileOperationsService
                .CreateDirectoryAsync(destDir, cancellationToken)
                .ConfigureAwait(false);
            ManifestPathPolicy.EnsureNoReparsePointDescendants(fileOperationsService, destinationPath, destDir);
        }

        await fileOperationsService
            .WriteFileAtomicallyAsync(
                destFilePath,
                (destStream, token) =>
                    VerifyFileChunksAsync(
                        fileEntry,
                        chunksDir,
                        encryptionKey,
                        namingKey,
                        encryptionStrategy,
                        storedChunkNonces,
                        compressionStrategy,
                        destStream,
                        token
                    ),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Decrypts and authenticates every chunk of a file into <paramref name="destination"/>, then
    /// checks the reassembled size and SHA-256 hash against the manifest.
    /// </summary>
    /// <remarks>
    /// Each chunk is authenticated with its content hash concatenated with its nonce as associated
    /// data, so a tampered chunk fails before any of it reaches <paramref name="destination"/>, and
    /// decompression is bounded by the size the manifest declares for the file so a chunk cannot
    /// expand past it. Verification passes <see cref="Stream.Null"/> as the destination to discard
    /// the plaintext. Each chunk's decoded hash, nonce, associated data, ciphertext, and plaintext
    /// are zeroed once the chunk has been written, and the expected and computed file hashes are
    /// zeroed after they are compared.
    /// </remarks>
    /// <param name="fileEntry">The manifest entry describing the file and its chunks.</param>
    /// <param name="chunksDir">The directory the encrypted chunk files are read from.</param>
    /// <param name="encryptionKey">The chunk encryption sub-key.</param>
    /// <param name="namingKey">The sub-key each chunk's on-disk file name is derived from.</param>
    /// <param name="encryptionStrategy">The strategy used to decrypt chunks.</param>
    /// <param name="storedChunkNonces">The cache resolving a chunk hash to the nonce its stored ciphertext authenticates under.</param>
    /// <param name="compressionStrategy">The decompression strategy, or <see langword="null"/> if chunks are uncompressed.</param>
    /// <param name="destination">The stream the reconstructed plaintext is written to.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when every chunk has been verified and written to <paramref name="destination"/>.</returns>
    /// <exception cref="InvalidDataException">The entry path is malformed or a chunk decompresses beyond the declared size.</exception>
    /// <exception cref="CryptographicException">A chunk fails authentication or the size or hash does not match the manifest.</exception>
    private async Task VerifyFileChunksAsync(
        ChunkManifestFileEntry fileEntry,
        string chunksDir,
        byte[] encryptionKey,
        byte[] namingKey,
        IEncryptionAlgorithmStrategy encryptionStrategy,
        ConcurrentDictionary<string, Lazy<Task<string>>> storedChunkNonces,
        ICompressionStrategy? compressionStrategy,
        Stream destination,
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

            try
            {
                chunkHash = DecodeBase64FixedLength(
                    chunkRef.Hash,
                    SHA256.HashSizeInBytes,
                    "Invalid chunk hash."
                );
                var nonceB64 = storedChunkNonces.TryGetValue(chunkRef.Hash, out var storedChunk)
                    ? await storedChunk.Value.ConfigureAwait(false)
                    : chunkRef.Nonce;
                nonce = DecodeBase64FixedLength(
                    nonceB64,
                    EncryptionConstants.NonceSize,
                    "Invalid chunk nonce."
                );
                var chunkFilePath = this.ComputeChunkFilePath(chunksDir, namingKey, chunkHash);
                var storedSize = fileOperationsService.GetFileSize(chunkFilePath);
                var maximumStoredSize = compressionStrategy is null
                    ? checked(chunkRef.Size + EncryptionConstants.TagSize)
                    : MaximumStoredChunkSize;

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

                associatedData = ChunkCryptoHelper.BuildChunkAssociatedData(chunkHash, nonce);
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

        var expectedFileHash = DecodeBase64FixedLength(
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
    /// The budget bounds decompression to the size the manifest declares for the file, so a crafted
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
