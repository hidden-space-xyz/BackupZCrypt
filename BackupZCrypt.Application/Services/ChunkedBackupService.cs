using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using BackupZCrypt.Application.Resources;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Application.ValueObjects.Manifest;
using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Factories.Interfaces;
using BackupZCrypt.Domain.Resources;
using BackupZCrypt.Domain.Services.Interfaces;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Backup;

namespace BackupZCrypt.Application.Services;

internal sealed class ChunkedBackupService(
    ICompressionServiceFactory compressionServiceFactory,
    IEnumerable<IEncryptionAlgorithmStrategy> encryptionStrategies,
    IFileOperationsService fileOperationsService,
    IManifestService manifestService,
    IChunkingStrategy chunkingStrategy,
    IKeyDerivationServiceFactory keyDerivationServiceFactory
) : IChunkedBackupService
{
    private const int KeySizeBytes = EncryptionConstants.KeySize / 8;

    private static readonly StringComparison PathComparer = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static readonly char[] InvalidPathChars = Path.GetInvalidPathChars();

    private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();

    private readonly Dictionary<
        EncryptionAlgorithm,
        IEncryptionAlgorithmStrategy
    > encryptionStrategiesById = encryptionStrategies.ToDictionary(
        static strategy => strategy.Id,
        static strategy => strategy
    );

    public async Task<Result<BackupResult>> CreateAsync(
        string sourcePath,
        string destinationPath,
        BackupRequest request,
        IProgress<BackupStatus> progress,
        CancellationToken cancellationToken
    )
    {
        var stopwatch = Stopwatch.StartNew();

        var source = await ResolveSourceAsync(sourcePath, allowSingleFile: true, cancellationToken)
            .ConfigureAwait(false);

        if (source is null)
        {
            return Result<BackupResult>.Failure(Resources.Messages.SourcePathNotExist);
        }

        var (sourceFiles, sourceRoot, isFile) = source.Value;
        if (sourceFiles.Length == 0)
        {
            stopwatch.Stop();
            return Result<BackupResult>.Success(
                new BackupResult(
                    false,
                    stopwatch.Elapsed,
                    0,
                    0,
                    0,
                    errors: [Resources.Messages.NoFilesInSourceDirectory]
                )
            );
        }

        await fileOperationsService
            .CreateDirectoryAsync(destinationPath, cancellationToken)
            .ConfigureAwait(false);

        var chunksDir = fileOperationsService.CombinePath(
            destinationPath,
            BackupConstants.ChunksDirectoryName
        );

        await fileOperationsService
            .CreateDirectoryAsync(chunksDir, cancellationToken)
            .ConfigureAwait(false);

        byte[]? masterSalt = null;
        DerivedKeySet? keys = null;

        try
        {
            masterSalt = GenerateSalt();
            keys = DeriveKeySet(request.Password, masterSalt, request.KeyDerivationAlgorithm);

            var encryptionStrategy = ResolveEncryptionStrategy(request.EncryptionAlgorithm);
            var compressionStrategy = CreateCompressionStrategy(request.Compression);

            var totalFiles = sourceFiles.Length;
            var totalBytes = SumFileSizes(sourceFiles);

            progress?.Report(new BackupStatus(0, totalFiles, 0, totalBytes, TimeSpan.Zero));

            ConcurrentBag<ChunkManifestFileEntry> fileEntries = [];
            ConcurrentBag<string> errors = [];
            long processedBytes = 0;
            var processedFiles = 0;
            string? fatalError = null;
            ConcurrentDictionary<string, Lazy<Task<string>>> storedChunks = new(
                StringComparer.Ordinal
            );

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken
            );
            var maxDop = Math.Max(1, Environment.ProcessorCount);

            try
            {
                await Parallel
                    .ForEachAsync(
                        sourceFiles,
                        new ParallelOptions
                        {
                            MaxDegreeOfParallelism = maxDop,
                            CancellationToken = linkedCts.Token,
                        },
                        async (file, token) =>
                        {
                            try
                            {
                                var relativePath = isFile
                                    ? Path.GetFileName(file)
                                    : fileOperationsService.GetRelativePath(sourceRoot, file);

                                ValidateRelativeManifestPath(relativePath);
                                var fileSize = fileOperationsService.GetFileSize(file);

                                var entry = await ChunkAndEncryptFileAsync(
                                        file,
                                        relativePath,
                                        fileSize,
                                        chunksDir,
                                        keys.ChunkEncryptionKey,
                                        keys.ChunkNonceKey,
                                        keys.NamingKey,
                                        encryptionStrategy,
                                        compressionStrategy,
                                        storedChunks,
                                        token
                                    )
                                    .ConfigureAwait(false);

                                fileEntries.Add(entry);
                                Interlocked.Increment(ref processedFiles);
                                var currentBytes = Interlocked.Add(ref processedBytes, fileSize);

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
                                if (IsFileLevelError(ex))
                                {
                                    errors.Add(
                                        string.Format(
                                            Resources.Messages.EncryptionErrorFormat,
                                            file,
                                            ex.Message
                                        )
                                    );
                                }
                                else
                                {
                                    Interlocked.CompareExchange(ref fatalError, ex.Message, null);
                                    await linkedCts.CancelAsync().ConfigureAwait(false);
                                }
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

            ManifestHeader header = new(
                request.EncryptionAlgorithm,
                request.KeyDerivationAlgorithm,
                request.Compression
            );

            ChunkManifestData manifestData = new(
                header,
                Convert.ToBase64String(masterSalt),
                [.. fileEntries.OrderBy(static f => f.OriginalPath, StringComparer.Ordinal)]
            );

            var manifestErrors = await manifestService
                .SaveChunkManifestAsync(
                    manifestData,
                    destinationPath,
                    keys.ManifestEncryptionKey,
                    request.EncryptionAlgorithm,
                    cancellationToken
                )
                .ConfigureAwait(false);

            List<string> errorList = [.. errors];
            errorList.AddRange(manifestErrors);

            stopwatch.Stop();
            var isSuccess = errorList.Count == 0 && processedFiles == totalFiles;

            return errorList.Count > 0 && processedFiles == 0
                ? Result<BackupResult>.Failure(
                    string.Format(
                        Resources.Messages.AllFilesFailedFormat,
                        string.Join("; ", errorList)
                    )
                )
                : Result<BackupResult>.Success(
                    new BackupResult(
                        isSuccess,
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

            if (masterSalt is not null)
            {
                CryptographicOperations.ZeroMemory(masterSalt);
            }
        }
    }

    public async Task<Result<BackupResult>> UpdateAsync(
        string sourcePath,
        string destinationPath,
        BackupRequest request,
        IProgress<BackupStatus> progress,
        CancellationToken cancellationToken
    )
    {
        var stopwatch = Stopwatch.StartNew();

        var preamble = await manifestService
            .ReadChunkManifestPreambleAsync(destinationPath, cancellationToken)
            .ConfigureAwait(false);

        if (preamble is null)
        {
            return Result<BackupResult>.Failure(Resources.Messages.ManifestRequiredForUpdate);
        }

        request = request with
        {
            EncryptionAlgorithm = preamble.Algorithm,
            KeyDerivationAlgorithm = preamble.KeyDerivation,
        };

        DerivedKeySet? keys = null;

        try
        {
            keys = DeriveKeySet(
                request.Password,
                preamble.MasterSalt,
                request.KeyDerivationAlgorithm
            );

            var existingManifest = manifestService.DecryptChunkManifest(
                preamble,
                keys.ManifestEncryptionKey
            );

            if (existingManifest is null)
            {
                return Result<BackupResult>.Failure(Resources.Messages.ManifestRequiredForUpdate);
            }

            request = request with { Compression = existingManifest.Header.Compression };

            var source = await ResolveSourceAsync(
                    sourcePath,
                    allowSingleFile: false,
                    cancellationToken
                )
                .ConfigureAwait(false);

            if (source is null)
            {
                return Result<BackupResult>.Failure(Resources.Messages.SourcePathNotExist);
            }

            var (sourceFiles, sourceRoot, _) = source.Value;

            var encryptionStrategy = ResolveEncryptionStrategy(request.EncryptionAlgorithm);
            var compressionStrategy = CreateCompressionStrategy(request.Compression);

            var chunksDir = fileOperationsService.CombinePath(
                destinationPath,
                BackupConstants.ChunksDirectoryName
            );

            await fileOperationsService
                .CreateDirectoryAsync(chunksDir, cancellationToken)
                .ConfigureAwait(false);

            Dictionary<string, ChunkManifestFileEntry> existingFileIndex = new(
                StringComparer.FromComparison(PathComparer)
            );
            foreach (var entry in existingManifest.Files)
            {
                ValidateRelativeManifestPath(entry.OriginalPath);
                existingFileIndex[entry.OriginalPath] = entry;
            }

            ConcurrentBag<ChunkManifestFileEntry> updatedEntries = [];
            ConcurrentBag<string> errors = [];
            var processedFiles = 0;
            long processedBytes = 0;
            string? fatalError = null;

            List<(string File, string RelativePath, long Size)> filesToProcess = [];
            ConcurrentDictionary<string, byte> referencedChunkHashes = new(StringComparer.Ordinal);

            foreach (var file in sourceFiles)
            {
                var relativePath = fileOperationsService.GetRelativePath(sourceRoot, file);
                ValidateRelativeManifestPath(relativePath);
                var fileSize = fileOperationsService.GetFileSize(file);

                if (existingFileIndex.TryGetValue(relativePath, out var existing))
                {
                    var currentHash = await fileOperationsService
                        .ComputeFileHashAsync(file, cancellationToken)
                        .ConfigureAwait(false);

                    if (string.Equals(currentHash, existing.FileHash, StringComparison.Ordinal))
                    {
                        updatedEntries.Add(existing);
                        foreach (var chunk in existing.Chunks)
                        {
                            referencedChunkHashes.TryAdd(chunk.Hash, 0);
                        }

                        continue;
                    }
                }

                filesToProcess.Add((file, relativePath, fileSize));
            }

            var totalFilesToProcess = filesToProcess.Count;
            var totalBytes = filesToProcess.Sum(static f => f.Size);

            progress?.Report(
                new BackupStatus(0, totalFilesToProcess, 0, totalBytes, TimeSpan.Zero)
            );

            var storedChunks = BuildStoredChunkNonceCache(
                existingManifest.Files,
                chunksDir,
                keys.ChunkEncryptionKey,
                keys.NamingKey,
                encryptionStrategy,
                cancellationToken
            );

            if (totalFilesToProcess > 0)
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken
                );

                try
                {
                    await Parallel
                        .ForEachAsync(
                            filesToProcess,
                            new ParallelOptions
                            {
                                MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount),
                                CancellationToken = linkedCts.Token,
                            },
                            async (fileItem, token) =>
                            {
                                try
                                {
                                    var entry = await ChunkAndEncryptFileAsync(
                                            fileItem.File,
                                            fileItem.RelativePath,
                                            fileItem.Size,
                                            chunksDir,
                                            keys.ChunkEncryptionKey,
                                            keys.ChunkNonceKey,
                                            keys.NamingKey,
                                            encryptionStrategy,
                                            compressionStrategy,
                                            storedChunks,
                                            token
                                        )
                                        .ConfigureAwait(false);

                                    updatedEntries.Add(entry);
                                    foreach (var chunk in entry.Chunks)
                                    {
                                        referencedChunkHashes.TryAdd(chunk.Hash, 0);
                                    }

                                    Interlocked.Increment(ref processedFiles);
                                    var currentBytes = Interlocked.Add(
                                        ref processedBytes,
                                        fileItem.Size
                                    );

                                    progress?.Report(
                                        new BackupStatus(
                                            Volatile.Read(ref processedFiles),
                                            totalFilesToProcess,
                                            currentBytes,
                                            totalBytes,
                                            stopwatch.Elapsed
                                        )
                                    );
                                }
                                catch (Exception ex) when (ex is not OperationCanceledException)
                                {
                                    if (IsFileLevelError(ex))
                                    {
                                        errors.Add(
                                            string.Format(
                                                Resources.Messages.EncryptionErrorFormat,
                                                fileItem.File,
                                                ex.Message
                                            )
                                        );
                                    }
                                    else
                                    {
                                        Interlocked.CompareExchange(
                                            ref fatalError,
                                            ex.Message,
                                            null
                                        );
                                        await linkedCts.CancelAsync().ConfigureAwait(false);
                                    }
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
            }

            ManifestHeader header = new(
                request.EncryptionAlgorithm,
                request.KeyDerivationAlgorithm,
                request.Compression
            );

            var canonicalEntries = await CanonicalizeChunkEntriesAsync(updatedEntries, storedChunks)
                .ConfigureAwait(false);

            ChunkManifestData newManifest = new(
                header,
                existingManifest.MasterSalt,
                canonicalEntries
            );

            var manifestErrors = await manifestService
                .SaveChunkManifestAsync(
                    newManifest,
                    destinationPath,
                    keys.ManifestEncryptionKey,
                    request.EncryptionAlgorithm,
                    cancellationToken
                )
                .ConfigureAwait(false);

            // Orphaned chunks may still be referenced by the previous manifest, so they
            // are only deleted once the new manifest has been persisted successfully.
            if (manifestErrors.Count == 0)
            {
                await DeleteOrphanedChunksAsync(
                        chunksDir,
                        referencedChunkHashes.Keys,
                        keys.NamingKey,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }

            List<string> errorList = [.. errors];
            errorList.AddRange(manifestErrors);

            stopwatch.Stop();
            var isSuccess = errorList.Count == 0 && processedFiles == totalFilesToProcess;

            return Result<BackupResult>.Success(
                new BackupResult(
                    isSuccess,
                    stopwatch.Elapsed,
                    totalBytes,
                    processedFiles,
                    totalFilesToProcess,
                    errors: errorList
                )
            );
        }
        finally
        {
            keys?.Dispose();
        }
    }

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
            return Result<BackupResult>.Failure(Resources.Messages.ManifestRequiredForDecryption);
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
                return Result<BackupResult>.Failure(
                    Resources.Messages.ManifestRequiredForDecryption
                );
            }

            var encryptionStrategy = ResolveEncryptionStrategy(manifest.Header.EncryptionAlgorithm);

            var compressionStrategy = CreateCompressionStrategy(manifest.Header.Compression);

            var chunksDir = fileOperationsService.CombinePath(
                sourcePath,
                BackupConstants.ChunksDirectoryName
            );

            var storedChunkNonces = BuildStoredChunkNonceCache(
                manifest.Files,
                chunksDir,
                keys.ChunkEncryptionKey,
                keys.NamingKey,
                encryptionStrategy,
                cancellationToken
            );

            var totalFiles = manifest.Files.Count;
            var totalBytes = manifest.Files.Sum(static f => f.TotalSize);
            progress?.Report(new BackupStatus(0, totalFiles, 0, totalBytes, TimeSpan.Zero));

            ConcurrentBag<string> errors = [];
            long processedBytes = 0;
            var processedFiles = 0;
            string? fatalError = null;

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
                            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount),
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

                                Interlocked.Increment(ref processedFiles);
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
                            catch (CryptographicException)
                            {
                                Interlocked.CompareExchange(
                                    ref fatalError,
                                    Domain.Resources.Messages.InvalidPassword,
                                    null
                                );
                                await linkedCts.CancelAsync().ConfigureAwait(false);
                            }
                            catch (Exception ex) when (ex is not OperationCanceledException)
                            {
                                if (IsFileLevelError(ex))
                                {
                                    errors.Add(
                                        string.Format(
                                            Resources.Messages.EncryptionErrorFormat,
                                            fileEntry.OriginalPath,
                                            ex.Message
                                        )
                                    );
                                }
                                else
                                {
                                    Interlocked.CompareExchange(ref fatalError, ex.Message, null);
                                    await linkedCts.CancelAsync().ConfigureAwait(false);
                                }
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

            List<string> errorList = [.. errors];
            stopwatch.Stop();
            var isSuccess = errorList.Count == 0 && processedFiles == totalFiles;

            return errorList.Count > 0 && processedFiles == 0
                ? Result<BackupResult>.Failure(
                    string.Format(
                        Resources.Messages.AllFilesFailedFormat,
                        string.Join("; ", errorList)
                    )
                )
                : Result<BackupResult>.Success(
                    new BackupResult(
                        isSuccess,
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

    private async Task<ChunkManifestFileEntry> ChunkAndEncryptFileAsync(
        string filePath,
        string relativePath,
        long fileSize,
        string chunksDir,
        byte[] encryptionKey,
        byte[] nonceKey,
        byte[] namingKey,
        IEncryptionAlgorithmStrategy encryptionStrategy,
        ICompressionStrategy? compressionStrategy,
        ConcurrentDictionary<string, Lazy<Task<string>>> storedChunks,
        CancellationToken cancellationToken
    )
    {
        ValidateRelativeManifestPath(relativePath);
        List<ChunkManifestChunkRef> chunkRefs = [];

        await using var fileStream = fileOperationsService.OpenReadStream(
            filePath,
            StreamConstants.CopyBufferSize
        );

        using var fileHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        await foreach (
            var chunkData in chunkingStrategy
                .ChunkAsync(fileStream, cancellationToken)
                .ConfigureAwait(false)
        )
        {
            fileHasher.AppendData(chunkData.Span);

            var chunkHash = SHA256.HashData(chunkData.Span);
            var chunkHashB64 = Convert.ToBase64String(chunkHash);
            var chunkFileName = ComputeChunkFileName(namingKey, chunkHash);
            var chunkFilePath = fileOperationsService.CombinePath(
                chunksDir,
                chunkFileName + BackupConstants.AppFileExtension
            );

            var chunkOperation = new Lazy<Task<string>>(
                () =>
                    EncryptAndStoreChunkAsync(
                        chunkData,
                        chunkHash,
                        chunkFilePath,
                        encryptionKey,
                        nonceKey,
                        encryptionStrategy,
                        compressionStrategy,
                        cancellationToken
                    ),
                LazyThreadSafetyMode.ExecutionAndPublication
            );

            var storedChunk = storedChunks.GetOrAdd(chunkHashB64, chunkOperation);

            var nonceB64 = await AwaitStoredChunkNonceAsync(
                    chunkHashB64,
                    storedChunk,
                    chunkOperation,
                    storedChunks
                )
                .ConfigureAwait(false);

            chunkRefs.Add(new ChunkManifestChunkRef(chunkHashB64, chunkData.Length, nonceB64));
            CryptographicOperations.ZeroMemory(chunkHash);
        }

        var fileHash = fileHasher.GetHashAndReset();
        var fileHashB64 = Convert.ToBase64String(fileHash);
        CryptographicOperations.ZeroMemory(fileHash);

        return new ChunkManifestFileEntry(relativePath, fileHashB64, fileSize, chunkRefs);
    }

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
        ValidateRelativeManifestPath(fileEntry.OriginalPath);

        var destFilePath = ResolveSafeDestinationPath(destinationPath, fileEntry.OriginalPath);
        var destDir = fileOperationsService.GetDirectoryName(destFilePath);

        if (!string.IsNullOrEmpty(destDir))
        {
            await fileOperationsService
                .CreateDirectoryAsync(destDir, cancellationToken)
                .ConfigureAwait(false);
        }

        await using var destStream = fileOperationsService.CreateWriteStream(
            destFilePath,
            StreamConstants.CopyBufferSize
        );

        using var fileHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long restoredBytes = 0;

        foreach (var chunkRef in fileEntry.Chunks)
        {
            var chunkHash = DecodeBase64FixedLength(
                chunkRef.Hash,
                KeySizeBytes,
                "Invalid chunk hash."
            );
            var nonceB64 = storedChunkNonces.TryGetValue(chunkRef.Hash, out var storedChunk)
                ? await storedChunk.Value.ConfigureAwait(false)
                : chunkRef.Nonce;
            var nonce = DecodeBase64FixedLength(
                nonceB64,
                EncryptionConstants.NonceSize,
                "Invalid chunk nonce."
            );
            var chunkFileName = ComputeChunkFileName(namingKey, chunkHash);
            var chunkFilePath = fileOperationsService.CombinePath(
                chunksDir,
                chunkFileName + BackupConstants.AppFileExtension
            );

            var encryptedData = await fileOperationsService
                .ReadAllBytesAsync(chunkFilePath, cancellationToken)
                .ConfigureAwait(false);

            var associatedData = BuildChunkAssociatedData(chunkHash, nonce);
            var decryptedData = encryptionStrategy.DecryptChunk(
                encryptedData,
                encryptionKey,
                nonce,
                associatedData
            );

            try
            {
                if (compressionStrategy is not null)
                {
                    await using MemoryStream compressedStream = new(decryptedData, writable: false);

                    await using var decompressedStream = await compressionStrategy
                        .DecompressAsync(compressedStream, cancellationToken)
                        .ConfigureAwait(false);

                    restoredBytes += await CopyToWithHashAsync(
                            decompressedStream,
                            destStream,
                            fileHasher,
                            fileEntry.TotalSize - restoredBytes,
                            cancellationToken
                        )
                        .ConfigureAwait(false);
                }
                else
                {
                    fileHasher.AppendData(decryptedData);
                    await destStream
                        .WriteAsync(decryptedData, cancellationToken)
                        .ConfigureAwait(false);
                    restoredBytes += decryptedData.Length;
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(chunkHash);
                CryptographicOperations.ZeroMemory(nonce);
                CryptographicOperations.ZeroMemory(associatedData);
                CryptographicOperations.ZeroMemory(encryptedData);
                CryptographicOperations.ZeroMemory(decryptedData);
            }
        }

        if (restoredBytes != fileEntry.TotalSize)
        {
            throw new CryptographicException("Restored file size does not match the manifest.");
        }

        var expectedFileHash = DecodeBase64FixedLength(
            fileEntry.FileHash,
            KeySizeBytes,
            "Invalid file hash."
        );
        var actualFileHash = fileHasher.GetHashAndReset();

        try
        {
            if (!CryptographicOperations.FixedTimeEquals(expectedFileHash, actualFileHash))
            {
                throw new CryptographicException("Restored file hash does not match the manifest.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expectedFileHash);
            CryptographicOperations.ZeroMemory(actualFileHash);
        }
    }

    private async Task<string> EncryptAndStoreChunkAsync(
        ReadOnlyMemory<byte> chunkData,
        byte[] chunkHash,
        string chunkFilePath,
        byte[] encryptionKey,
        byte[] nonceKey,
        IEncryptionAlgorithmStrategy encryptionStrategy,
        ICompressionStrategy? compressionStrategy,
        CancellationToken cancellationToken
    )
    {
        var nonce = ComputeChunkNonce(nonceKey, chunkHash);
        var nonceB64 = Convert.ToBase64String(nonce);
        byte[]? dataToEncrypt = null;
        byte[]? encrypted = null;
        byte[]? associatedData = null;

        try
        {
            if (compressionStrategy is not null)
            {
                await using var inputStream = CreateReadOnlyStream(chunkData);

                await using var compressedStream = await compressionStrategy
                    .CompressAsync(inputStream, cancellationToken)
                    .ConfigureAwait(false);

                if (compressedStream is MemoryStream compressedMemory)
                {
                    dataToEncrypt = compressedMemory.ToArray();
                }
                else
                {
                    await using MemoryStream compressedBuffer = new();
                    await compressedStream
                        .CopyToAsync(compressedBuffer, cancellationToken)
                        .ConfigureAwait(false);

                    dataToEncrypt = compressedBuffer.ToArray();
                }
            }

            associatedData = BuildChunkAssociatedData(chunkHash, nonce);
            encrypted = dataToEncrypt is not null
                ? encryptionStrategy.EncryptChunk(
                    dataToEncrypt,
                    encryptionKey,
                    nonce,
                    associatedData
                )
                : encryptionStrategy.EncryptChunk(
                    chunkData.Span,
                    encryptionKey,
                    nonce,
                    associatedData
                );

            await fileOperationsService
                .WriteAllBytesAsync(chunkFilePath, encrypted, cancellationToken)
                .ConfigureAwait(false);

            return nonceB64;
        }
        finally
        {
            if (dataToEncrypt is not null)
            {
                CryptographicOperations.ZeroMemory(dataToEncrypt);
            }

            if (encrypted is not null)
            {
                CryptographicOperations.ZeroMemory(encrypted);
            }

            if (associatedData is not null)
            {
                CryptographicOperations.ZeroMemory(associatedData);
            }

            CryptographicOperations.ZeroMemory(nonce);
        }
    }

    private ConcurrentDictionary<string, Lazy<Task<string>>> BuildStoredChunkNonceCache(
        IReadOnlyList<ChunkManifestFileEntry> manifestFiles,
        string chunksDir,
        byte[] encryptionKey,
        byte[] namingKey,
        IEncryptionAlgorithmStrategy encryptionStrategy,
        CancellationToken cancellationToken
    )
    {
        var chunkNonceCandidates = BuildChunkNonceCandidates(manifestFiles);
        ConcurrentDictionary<string, Lazy<Task<string>>> storedChunks = new(StringComparer.Ordinal);

        foreach (var (chunkHashB64, nonceCandidates) in chunkNonceCandidates)
        {
            var candidateCopy = nonceCandidates.ToArray();
            var storedChunk = new Lazy<Task<string>>(
                () =>
                    ResolveChunkNonceAsync(
                        chunkHashB64,
                        candidateCopy,
                        chunksDir,
                        encryptionKey,
                        namingKey,
                        encryptionStrategy,
                        cancellationToken
                    ),
                LazyThreadSafetyMode.ExecutionAndPublication
            );

            storedChunks.TryAdd(chunkHashB64, storedChunk);
        }

        return storedChunks;
    }

    private async Task<string> ResolveChunkNonceAsync(
        string chunkHashB64,
        string[] nonceCandidates,
        string chunksDir,
        byte[] encryptionKey,
        byte[] namingKey,
        IEncryptionAlgorithmStrategy encryptionStrategy,
        CancellationToken cancellationToken
    )
    {
        if (nonceCandidates.Length == 0)
        {
            throw new CryptographicException();
        }

        if (nonceCandidates.Length == 1)
        {
            return nonceCandidates[0];
        }

        var chunkHash = DecodeBase64FixedLength(chunkHashB64, KeySizeBytes, "Invalid chunk hash.");
        var chunkFileName = ComputeChunkFileName(namingKey, chunkHash);
        var chunkFilePath = fileOperationsService.CombinePath(
            chunksDir,
            chunkFileName + BackupConstants.AppFileExtension
        );
        var encryptedData = await fileOperationsService
            .ReadAllBytesAsync(chunkFilePath, cancellationToken)
            .ConfigureAwait(false);
        CryptographicException? lastException = null;

        try
        {
            foreach (var nonceCandidate in nonceCandidates)
            {
                byte[]? decryptedData = null;
                byte[]? nonce = null;
                byte[]? associatedData = null;

                try
                {
                    nonce = DecodeBase64FixedLength(
                        nonceCandidate,
                        EncryptionConstants.NonceSize,
                        "Invalid chunk nonce."
                    );
                    associatedData = BuildChunkAssociatedData(chunkHash, nonce);
                    decryptedData = encryptionStrategy.DecryptChunk(
                        encryptedData,
                        encryptionKey,
                        nonce,
                        associatedData
                    );

                    return nonceCandidate;
                }
                catch (FormatException ex)
                {
                    lastException = new CryptographicException(ex.Message, ex);
                }
                catch (CryptographicException ex)
                {
                    lastException = ex;
                }
                finally
                {
                    if (decryptedData is not null)
                    {
                        CryptographicOperations.ZeroMemory(decryptedData);
                    }

                    if (nonce is not null)
                    {
                        CryptographicOperations.ZeroMemory(nonce);
                    }

                    if (associatedData is not null)
                    {
                        CryptographicOperations.ZeroMemory(associatedData);
                    }
                }
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(chunkHash);
            CryptographicOperations.ZeroMemory(encryptedData);
        }

        throw lastException ?? new CryptographicException();
    }

    private static Dictionary<string, string[]> BuildChunkNonceCandidates(
        IReadOnlyList<ChunkManifestFileEntry> manifestFiles
    )
    {
        Dictionary<string, List<string>> chunkNonceCandidates = new(StringComparer.Ordinal);

        foreach (var file in manifestFiles)
        {
            ValidateRelativeManifestPath(file.OriginalPath);

            foreach (var chunk in file.Chunks)
            {
                var decodedHash = DecodeBase64FixedLength(
                    chunk.Hash,
                    KeySizeBytes,
                    "Invalid chunk hash."
                );
                var decodedNonce = DecodeBase64FixedLength(
                    chunk.Nonce,
                    EncryptionConstants.NonceSize,
                    "Invalid chunk nonce."
                );
                CryptographicOperations.ZeroMemory(decodedHash);
                CryptographicOperations.ZeroMemory(decodedNonce);

                if (!chunkNonceCandidates.TryGetValue(chunk.Hash, out var nonceCandidates))
                {
                    nonceCandidates = [];
                    chunkNonceCandidates.Add(chunk.Hash, nonceCandidates);
                }

                if (!nonceCandidates.Contains(chunk.Nonce, StringComparer.Ordinal))
                {
                    nonceCandidates.Add(chunk.Nonce);
                }
            }
        }

        return chunkNonceCandidates.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.ToArray(),
            StringComparer.Ordinal
        );
    }

    private static async Task<string> AwaitStoredChunkNonceAsync(
        string chunkHashB64,
        Lazy<Task<string>> storedChunk,
        Lazy<Task<string>> candidateChunk,
        ConcurrentDictionary<string, Lazy<Task<string>>> storedChunks
    )
    {
        try
        {
            return await storedChunk.Value.ConfigureAwait(false);
        }
        catch
        {
            if (ReferenceEquals(storedChunk, candidateChunk))
            {
                storedChunks.TryRemove(
                    new KeyValuePair<string, Lazy<Task<string>>>(chunkHashB64, candidateChunk)
                );
            }

            throw;
        }
    }

    private static async Task<IReadOnlyList<ChunkManifestFileEntry>> CanonicalizeChunkEntriesAsync(
        IEnumerable<ChunkManifestFileEntry> entries,
        ConcurrentDictionary<string, Lazy<Task<string>>> storedChunks
    )
    {
        List<ChunkManifestFileEntry> canonicalEntries = [];

        foreach (var entry in entries.OrderBy(static e => e.OriginalPath, StringComparer.Ordinal))
        {
            List<ChunkManifestChunkRef> canonicalChunks = [];

            foreach (var chunk in entry.Chunks)
            {
                var nonce = storedChunks.TryGetValue(chunk.Hash, out var storedChunk)
                    ? await storedChunk.Value.ConfigureAwait(false)
                    : chunk.Nonce;

                canonicalChunks.Add(new ChunkManifestChunkRef(chunk.Hash, chunk.Size, nonce));
            }

            canonicalEntries.Add(
                new ChunkManifestFileEntry(
                    entry.OriginalPath,
                    entry.FileHash,
                    entry.TotalSize,
                    canonicalChunks
                )
            );
        }

        return canonicalEntries;
    }

    private IEncryptionAlgorithmStrategy ResolveEncryptionStrategy(EncryptionAlgorithm algorithm)
    {
        return !encryptionStrategiesById.TryGetValue(algorithm, out var strategy)
            ? throw new ArgumentOutOfRangeException(
                nameof(algorithm),
                string.Format(
                    Domain.Resources.Messages.EncryptionAlgorithmNotRegisteredFormat,
                    algorithm
                )
            )
            : strategy;
    }

    private async Task DeleteOrphanedChunksAsync(
        string chunksDir,
        IEnumerable<string> referencedChunkHashes,
        byte[] namingKey,
        CancellationToken cancellationToken
    )
    {
        try
        {
            HashSet<string> expectedFileNames = new(StringComparer.OrdinalIgnoreCase);
            foreach (var hash in referencedChunkHashes)
            {
                var hashBytes = DecodeBase64FixedLength(hash, KeySizeBytes, "Invalid chunk hash.");
                try
                {
                    var fileName =
                        ComputeChunkFileName(namingKey, hashBytes)
                        + BackupConstants.AppFileExtension;
                    expectedFileNames.Add(fileName);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(hashBytes);
                }
            }

            if (!fileOperationsService.DirectoryExists(chunksDir))
            {
                return;
            }

            var existingFiles = await fileOperationsService
                .GetFilesAsync(chunksDir, "*" + BackupConstants.AppFileExtension, cancellationToken)
                .ConfigureAwait(false);

            foreach (var file in existingFiles)
            {
                var fileName = Path.GetFileName(file);
                if (!expectedFileNames.Contains(fileName))
                {
                    try
                    {
                        fileOperationsService.DeleteFile(file);
                    }
                    catch
                    {
                        // Best-effort cleanup.
                    }
                }
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private async Task<(
        string[] SourceFiles,
        string SourceRoot,
        bool IsSingleFile
    )?> ResolveSourceAsync(
        string sourcePath,
        bool allowSingleFile,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        var isFile = fileOperationsService.FileExists(sourcePath);
        var isDirectory = fileOperationsService.DirectoryExists(sourcePath);

        if (isFile)
        {
            if (!allowSingleFile)
            {
                return null;
            }

            var sourceRoot = fileOperationsService.GetDirectoryName(sourcePath) ?? string.Empty;
            return ([sourcePath], sourceRoot, true);
        }

        if (!isDirectory)
        {
            return null;
        }

        var sourceFiles = await fileOperationsService
            .GetFilesAsync(sourcePath, "*", cancellationToken)
            .ConfigureAwait(false);

        Array.Sort(sourceFiles, StringComparer.FromComparison(PathComparer));
        return (sourceFiles, sourcePath, false);
    }

    private ICompressionStrategy? CreateCompressionStrategy(CompressionMode compressionMode)
    {
        return compressionMode == CompressionMode.None
            ? null
            : compressionServiceFactory.Create(compressionMode);
    }

    private DerivedKeySet DeriveKeySet(string password, byte[] salt, KeyDerivationAlgorithm kdf)
    {
        var strategy = keyDerivationServiceFactory.Create(kdf);
        var masterKey = strategy.DeriveKey(password, salt, KeySizeBytes * 8);

        try
        {
            return new DerivedKeySet(
                masterKey,
                DeriveSubKey(masterKey, "chunk-encryption"u8),
                DeriveSubKey(masterKey, "chunk-nonce"u8),
                DeriveSubKey(masterKey, "chunk-naming"u8),
                DeriveSubKey(masterKey, "manifest-encryption"u8)
            );
        }
        catch
        {
            CryptographicOperations.ZeroMemory(masterKey);
            throw;
        }
    }

    private static byte[] DeriveSubKey(byte[] masterKey, ReadOnlySpan<byte> context)
    {
        var subKey = new byte[KeySizeBytes];
        HKDF.Expand(HashAlgorithmName.SHA256, masterKey, subKey, context);
        return subKey;
    }

    private static MemoryStream CreateReadOnlyStream(ReadOnlyMemory<byte> data)
    {
        return System.Runtime.InteropServices.MemoryMarshal.TryGetArray(data, out var segment)
            ? new MemoryStream(segment.Array!, segment.Offset, segment.Count, writable: false)
            : new MemoryStream(data.ToArray(), writable: false);
    }

    private static byte[] GenerateSalt()
    {
        var salt = new byte[EncryptionConstants.SaltSize];
        RandomNumberGenerator.Fill(salt);
        return salt;
    }

    private static byte[] ComputeChunkNonce(byte[] nonceKey, byte[] chunkHash)
    {
        var hmac = HMACSHA256.HashData(nonceKey, chunkHash);
        var nonce = new byte[EncryptionConstants.NonceSize];

        try
        {
            Buffer.BlockCopy(hmac, 0, nonce, 0, EncryptionConstants.NonceSize);
            return nonce;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(hmac);
        }
    }

    private static string ComputeChunkFileName(byte[] namingKey, byte[] chunkHash)
    {
        var hmac = HMACSHA256.HashData(namingKey, chunkHash);
        try
        {
            return Convert.ToHexStringLower(hmac);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(hmac);
        }
    }

    private static byte[] BuildChunkAssociatedData(byte[] chunkHash, byte[] nonce)
    {
        var ad = new byte[chunkHash.Length + nonce.Length];
        chunkHash.CopyTo(ad, 0);
        nonce.CopyTo(ad, chunkHash.Length);
        return ad;
    }

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

                if (read == 0)
                {
                    return total;
                }

                total += read;

                // Stops decompression bombs before they reach the destination file:
                // a genuine chunk can never exceed the size declared in the manifest.
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

    private long SumFileSizes(IEnumerable<string> files)
    {
        long total = 0;

        foreach (var file in files)
        {
            try
            {
                checked
                {
                    total += fileOperationsService.GetFileSize(file);
                }
            }
            catch
            {
                // Preserve previous behavior: inaccessible files contribute zero here
                // and are reported during actual processing.
            }
        }

        return total;
    }

    private static byte[] DecodeBase64FixedLength(
        string value,
        int expectedLength,
        string errorMessage
    )
    {
        byte[] decoded;

        try
        {
            decoded = Convert.FromBase64String(value);
        }
        catch (FormatException ex)
        {
            throw new CryptographicException(errorMessage, ex);
        }

        if (decoded.Length != expectedLength)
        {
            CryptographicOperations.ZeroMemory(decoded);
            throw new CryptographicException(errorMessage);
        }

        return decoded;
    }

    private static void ValidateRelativeManifestPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new InvalidDataException("Manifest entry path is empty.");
        }

        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException("Manifest entry path must be relative.");
        }

        if (relativePath.IndexOfAny(InvalidPathChars) >= 0)
        {
            throw new InvalidDataException("Manifest entry path contains invalid characters.");
        }

        var pathSegments = relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries
        );

        if (pathSegments.Any(static segment => segment == ".."))
        {
            throw new InvalidDataException("Manifest entry path contains traversal segments.");
        }

        // On Windows, characters such as ':' would silently redirect writes to NTFS
        // alternate data streams; reject any segment that is not a valid file name.
        if (
            OperatingSystem.IsWindows()
            && pathSegments.Any(static segment => segment.IndexOfAny(InvalidFileNameChars) >= 0)
        )
        {
            throw new InvalidDataException(
                "Manifest entry path contains invalid file name characters."
            );
        }
    }

    private static string ResolveSafeDestinationPath(string destinationRoot, string relativePath)
    {
        ValidateRelativeManifestPath(relativePath);

        var rootFullPath = Path.GetFullPath(destinationRoot);
        var destinationFullPath = Path.GetFullPath(Path.Combine(rootFullPath, relativePath));
        var rootWithSeparator = EnsureTrailingDirectorySeparator(rootFullPath);

        if (!destinationFullPath.StartsWith(rootWithSeparator, PathComparer))
        {
            throw new InvalidDataException("Manifest entry path escapes the restore directory.");
        }

        return destinationFullPath;
    }

    private static string EnsureTrailingDirectorySeparator(string path)
    {
        return
            path.EndsWith(Path.DirectorySeparatorChar)
            || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static bool IsFileLevelError(Exception ex)
    {
        return ex
            is FileNotFoundException
                or DirectoryNotFoundException
                or PathTooLongException
                or IOException { Message: not null }
                or UnauthorizedAccessException;
    }

    private sealed class DerivedKeySet : IDisposable
    {
        public DerivedKeySet(
            byte[] masterKey,
            byte[] chunkEncryptionKey,
            byte[] chunkNonceKey,
            byte[] namingKey,
            byte[] manifestEncryptionKey
        )
        {
            MasterKey = masterKey;
            ChunkEncryptionKey = chunkEncryptionKey;
            ChunkNonceKey = chunkNonceKey;
            NamingKey = namingKey;
            ManifestEncryptionKey = manifestEncryptionKey;
        }

        public byte[] MasterKey { get; }

        public byte[] ChunkEncryptionKey { get; }

        public byte[] ChunkNonceKey { get; }

        public byte[] NamingKey { get; }

        public byte[] ManifestEncryptionKey { get; }

        public void Dispose()
        {
            CryptographicOperations.ZeroMemory(MasterKey);
            CryptographicOperations.ZeroMemory(ChunkEncryptionKey);
            CryptographicOperations.ZeroMemory(ChunkNonceKey);
            CryptographicOperations.ZeroMemory(NamingKey);
            CryptographicOperations.ZeroMemory(ManifestEncryptionKey);
        }
    }
}
