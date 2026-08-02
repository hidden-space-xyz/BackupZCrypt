using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;

using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.Utilities.Helpers;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Application.ValueObjects.Manifest;
using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Factories.Interfaces;
using BackupZCrypt.Domain.Services.Interfaces;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Backup;
using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Application.Services;

/// <summary>
/// Implements chunk-based backup, update, restore, and verification. Files are split into
/// content-defined chunks that are deduplicated by content hash, optionally compressed, and
/// individually encrypted with per-chunk nonces; sub-keys for chunk encryption, nonce derivation,
/// chunk naming, and the manifest are derived from the password-derived master key via HKDF.
/// </summary>
/// <param name="compressionServiceFactory">The factory producing compression strategies for a compression mode.</param>
/// <param name="encryptionServiceFactory">The factory producing encryption strategies for an algorithm.</param>
/// <param name="fileOperationsService">The service used to read, write, and enumerate files.</param>
/// <param name="manifestService">The service used to read and write the encrypted backup manifest.</param>
/// <param name="chunkingStrategy">The strategy used to split file streams into content-defined chunks.</param>
/// <param name="keyDerivationServiceFactory">The factory producing key derivation services for an algorithm.</param>
internal sealed class ChunkedBackupService(
    ICompressionServiceFactory compressionServiceFactory,
    IEncryptionServiceFactory encryptionServiceFactory,
    IFileOperationsService fileOperationsService,
    IManifestService manifestService,
    IChunkingStrategy chunkingStrategy,
    IKeyDerivationServiceFactory keyDerivationServiceFactory
) : IChunkedBackupService
{
    /// <summary>
    /// The length in bytes of a 256-bit key, which is also the expected length of the SHA-256 chunk
    /// and file hashes decoded from the manifest.
    /// </summary>
    private const int KeySizeBytes = EncryptionConstants.KeySize / 8;

    /// <summary>
    /// The comparison applied to backup paths, shared with every other layer that compares them.
    /// </summary>
    private static readonly StringComparison PathComparer = PathNormalizationHelper.PathComparer;

    /// <summary>
    /// Creates a new chunked backup, processing files in parallel, deduplicating chunks by content,
    /// and persisting an encrypted manifest.
    /// </summary>
    /// <param name="sourcePath">The directory to back up.</param>
    /// <param name="destinationPath">The directory where the chunks directory and manifest are written.</param>
    /// <param name="request">The backup request carrying the password and algorithm choices.</param>
    /// <param name="progress">A sink that receives incremental status updates.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A result describing the backup outcome, including any per-file errors.</returns>
    public async Task<Result<BackupResult>> CreateAsync(
        string sourcePath,
        string destinationPath,
        BackupRequest request,
        IProgress<BackupStatus> progress,
        CancellationToken cancellationToken
    )
    {
        var stopwatch = Stopwatch.StartNew();

        var source = await ResolveSourceAsync(sourcePath, cancellationToken)
            .ConfigureAwait(false);

        if (source is null)
        {
            return Result<BackupResult>.Failure(MessageCode.SourcePathNotExist);
        }

        var (sourceFiles, sourceRoot) = source.Value;
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
                    errors: [new LocalizableMessage(MessageCode.NoFilesInSourceDirectory)]
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

            var encryptionStrategy = encryptionServiceFactory.Create(request.EncryptionAlgorithm);
            var compressionStrategy = CreateCompressionStrategy(request.Compression);

            var totalFiles = sourceFiles.Length;
            var totalBytes = SumFileSizes(sourceFiles);

            progress?.Report(new BackupStatus(0, totalFiles, 0, totalBytes, TimeSpan.Zero));

            ConcurrentBag<ChunkManifestFileEntry> fileEntries = [];
            ConcurrentBag<LocalizableMessage> errors = [];
            long processedBytes = 0;
            var processedFiles = 0;
            LocalizableMessage? fatalError = null;
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
                                var relativePath = ManifestPathPolicy.ToManifestPath(
                                    fileOperationsService.GetRelativePath(sourceRoot, file)
                                );

                                ManifestPathPolicy.ValidateRelative(relativePath);
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
                                _ = Interlocked.Increment(ref processedFiles);
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
                                        new LocalizableMessage(
                                            MessageCode.EncryptionErrorFormat,
                                            file,
                                            ex.Message
                                        )
                                    );
                                }
                                else
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
                        }
                    )
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (fatalError is not null)
            {
                stopwatch.Stop();
                return Result<BackupResult>.Failure(fatalError!);
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

            List<LocalizableMessage> errorList = [.. errors];
            errorList.AddRange(manifestErrors);

            stopwatch.Stop();
            var isSuccess = errorList.Count == 0 && processedFiles == totalFiles;

            return errorList.Count > 0 && processedFiles == 0
                ? Result<BackupResult>.Failure(
                    [new LocalizableMessage(MessageCode.AllFilesFailed), .. errorList]
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

    /// <summary>
    /// Updates an existing chunked backup by re-chunking only files whose content hash changed,
    /// rewriting the manifest, and deleting chunks no longer referenced once the manifest is saved.
    /// The encryption, key derivation, and compression settings are taken from the existing backup.
    /// </summary>
    /// <remarks>
    /// An update reuses the algorithms the archive was written with, never the ones
    /// <paramref name="request"/> carries: a different key derivation function produces a different
    /// master key and the archive stops opening. Those values are therefore read from the manifest
    /// preamble and header at each point of use, leaving <paramref name="request"/> meaning what the
    /// user asked for rather than what the archive happens to be.
    /// </remarks>
    /// <param name="sourcePath">The source directory whose current state is compared against the backup.</param>
    /// <param name="destinationPath">The directory containing the existing backup to update.</param>
    /// <param name="request">The backup request carrying the password used to open the manifest.</param>
    /// <param name="progress">A sink that receives incremental status updates.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A result describing the update outcome, including any per-file errors.</returns>
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
            return Result<BackupResult>.Failure(MessageCode.ManifestRequiredForUpdate);
        }

        DerivedKeySet? keys = null;

        try
        {
            keys = DeriveKeySet(request.Password, preamble.MasterSalt, preamble.KeyDerivation);

            var existingManifest = manifestService.DecryptChunkManifest(
                preamble,
                keys.ManifestEncryptionKey
            );

            if (existingManifest is null)
            {
                return Result<BackupResult>.Failure(MessageCode.InvalidPassword);
            }

            var source = await ResolveSourceAsync(sourcePath, cancellationToken)
                .ConfigureAwait(false);

            if (source is null)
            {
                return Result<BackupResult>.Failure(MessageCode.SourcePathNotExist);
            }

            var (sourceFiles, sourceRoot) = source.Value;

            var encryptionStrategy = encryptionServiceFactory.Create(preamble.Algorithm);
            var compressionStrategy = CreateCompressionStrategy(
                existingManifest.Header.Compression
            );

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
                ManifestPathPolicy.ValidateRelative(entry.OriginalPath);
                existingFileIndex[entry.OriginalPath] = entry;
            }

            ConcurrentBag<ChunkManifestFileEntry> updatedEntries = [];
            ConcurrentBag<LocalizableMessage> errors = [];
            var processedFiles = 0;
            long processedBytes = 0;
            LocalizableMessage? fatalError = null;

            List<(string File, string RelativePath, long Size)> filesToProcess = [];
            ConcurrentDictionary<string, byte> referencedChunkHashes = new(StringComparer.Ordinal);

            foreach (var file in sourceFiles)
            {
                var relativePath = ManifestPathPolicy.ToManifestPath(
                    fileOperationsService.GetRelativePath(sourceRoot, file)
                );
                ManifestPathPolicy.ValidateRelative(relativePath);
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
                            _ = referencedChunkHashes.TryAdd(chunk.Hash, 0);
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

            ValidateManifestEntries(existingManifest.Files);

            var storedChunks = BuildStoredChunkNonceCache(existingManifest.Files);

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
                                        _ = referencedChunkHashes.TryAdd(chunk.Hash, 0);
                                    }

                                    _ = Interlocked.Increment(ref processedFiles);
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
                                            new LocalizableMessage(
                                                MessageCode.EncryptionErrorFormat,
                                                fileItem.File,
                                                ex.Message
                                            )
                                        );
                                    }
                                    else
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
                            }
                        )
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (fatalError is not null)
                {
                    stopwatch.Stop();
                    return Result<BackupResult>.Failure(fatalError!);
                }
            }

            ManifestHeader header = new(
                preamble.Algorithm,
                preamble.KeyDerivation,
                existingManifest.Header.Compression
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
                    preamble.Algorithm,
                    cancellationToken
                )
                .ConfigureAwait(false);

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

            List<LocalizableMessage> errorList = [.. errors];
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
                            catch (CryptographicException)
                            {
                                _ = Interlocked.CompareExchange(
                                    ref fatalError,
                                    new LocalizableMessage(MessageCode.InvalidPassword),
                                    null
                                );
                                await linkedCts.CancelAsync().ConfigureAwait(false);
                            }
                            catch (Exception ex) when (ex is not OperationCanceledException)
                            {
                                if (IsFileLevelError(ex))
                                {
                                    errors.Add(
                                        new LocalizableMessage(
                                            MessageCode.DecryptionErrorFormat,
                                            fileEntry.OriginalPath,
                                            ex.Message
                                        )
                                    );
                                }
                                else
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
                        }
                    )
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (fatalError is not null)
            {
                stopwatch.Stop();
                return Result<BackupResult>.Failure(fatalError!);
            }

            List<LocalizableMessage> errorList = [.. errors];
            stopwatch.Stop();
            var isSuccess = errorList.Count == 0 && processedFiles == totalFiles;

            return errorList.Count > 0 && processedFiles == 0
                ? Result<BackupResult>.Failure(
                    [new LocalizableMessage(MessageCode.AllFilesFailed), .. errorList]
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
                        MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount),
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
            var isSuccess = errorList.Count == 0 && processedFiles == totalFiles;

            return Result<BackupResult>.Success(
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

    /// <summary>
    /// Splits a file into content-defined chunks, stores each chunk encrypted on disk, hashes the
    /// whole file, and returns the manifest entry that reconstructs it.
    /// </summary>
    /// <remarks>
    /// Chunks are deduplicated through <paramref name="storedChunks"/>, so a chunk already being
    /// stored for another file is awaited instead of being encrypted and written a second time.
    /// </remarks>
    /// <param name="filePath">The absolute path of the file to read.</param>
    /// <param name="relativePath">The file's path relative to the backup root, as recorded in the manifest.</param>
    /// <param name="fileSize">The file size in bytes recorded in the manifest entry.</param>
    /// <param name="chunksDir">The directory encrypted chunk files are written into.</param>
    /// <param name="encryptionKey">The chunk encryption sub-key.</param>
    /// <param name="nonceKey">The sub-key each chunk's deterministic nonce is derived from.</param>
    /// <param name="namingKey">The sub-key each chunk's on-disk file name is derived from.</param>
    /// <param name="encryptionStrategy">The strategy used to encrypt chunks.</param>
    /// <param name="compressionStrategy">The strategy applied before encryption, or <see langword="null"/> to skip compression.</param>
    /// <param name="storedChunks">The shared cache mapping a chunk hash to its in-flight or completed store operation.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The manifest entry describing the file and its ordered chunk references.</returns>
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
        ManifestPathPolicy.ValidateRelative(relativePath);
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
            await fileOperationsService
                .CreateDirectoryAsync(destDir, cancellationToken)
                .ConfigureAwait(false);
        }

        await using var destStream = fileOperationsService.CreateWriteStream(
            destFilePath,
            StreamConstants.CopyBufferSize
        );

        await VerifyFileChunksAsync(
                fileEntry,
                chunksDir,
                encryptionKey,
                namingKey,
                encryptionStrategy,
                storedChunkNonces,
                compressionStrategy,
                destStream,
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

            var associatedData = ChunkCryptoHelper.BuildChunkAssociatedData(chunkHash, nonce);
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

                    processedBytes += await CopyToWithHashAsync(
                            decompressedStream,
                            destination,
                            fileHasher,
                            fileEntry.TotalSize - processedBytes,
                            cancellationToken
                        )
                        .ConfigureAwait(false);
                }
                else
                {
                    fileHasher.AppendData(decryptedData);
                    await destination
                        .WriteAsync(decryptedData, cancellationToken)
                        .ConfigureAwait(false);
                    processedBytes += decryptedData.Length;
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

        if (processedBytes != fileEntry.TotalSize)
        {
            throw new CryptographicException("File size does not match the manifest.");
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
    /// Compresses a chunk when compression is enabled, encrypts it, and writes the ciphertext to its
    /// content-addressed file.
    /// </summary>
    /// <remarks>
    /// Compression runs before encryption so ciphertext is never compressed. The nonce is derived
    /// deterministically from the chunk hash, which lets identical content deduplicate while keeping
    /// the nonce key-dependent, and the chunk hash concatenated with that nonce is bound in as
    /// associated data. The compressed copy of the chunk, the ciphertext, the associated data, and
    /// the nonce are zeroed before returning; the plaintext buffer is owned by the caller.
    /// </remarks>
    /// <param name="chunkData">The plaintext bytes of the chunk.</param>
    /// <param name="chunkHash">The SHA-256 content hash of the chunk.</param>
    /// <param name="chunkFilePath">The full path the encrypted chunk is written to.</param>
    /// <param name="encryptionKey">The chunk encryption sub-key.</param>
    /// <param name="nonceKey">The sub-key the chunk nonce is derived from.</param>
    /// <param name="encryptionStrategy">The strategy used to encrypt the chunk.</param>
    /// <param name="compressionStrategy">The compression strategy, or <see langword="null"/> to store the chunk uncompressed.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The Base64-encoded nonce recorded for the chunk in the manifest.</returns>
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
        var nonce = ChunkCryptoHelper.ComputeChunkNonce(nonceKey, chunkHash);
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

            associatedData = ChunkCryptoHelper.BuildChunkAssociatedData(chunkHash, nonce);
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

    /// <summary>
    /// Builds the cache that maps each chunk hash referenced by a manifest to the nonce its stored
    /// ciphertext authenticates under.
    /// </summary>
    /// <remarks>
    /// A chunk's nonce is a deterministic function of the chunk-nonce sub-key and the chunk's content
    /// hash, and that sub-key is fixed for an archive's whole lifetime because the master salt is
    /// preserved across updates. One hash can therefore carry exactly one nonce; a manifest that
    /// records two for the same hash is rejected rather than guessed at. The entries are shared with
    /// the write path, which resolves its own asynchronously as chunks are stored.
    /// </remarks>
    /// <param name="manifestFiles">The manifest entries whose chunk references are indexed.</param>
    /// <returns>A cache keyed by Base64 chunk hash whose values resolve to the recorded Base64 nonce.</returns>
    /// <exception cref="CryptographicException">A chunk hash carries more than one distinct nonce.</exception>
    private static ConcurrentDictionary<string, Lazy<Task<string>>> BuildStoredChunkNonceCache(
        IReadOnlyList<ChunkManifestFileEntry> manifestFiles
    )
    {
        var chunkNonceCandidates = BuildChunkNonceCandidates(manifestFiles);
        ConcurrentDictionary<string, Lazy<Task<string>>> storedChunks = new(StringComparer.Ordinal);

        foreach (var (chunkHashB64, nonceCandidates) in chunkNonceCandidates)
        {
            if (nonceCandidates.Length != 1)
            {
                throw new CryptographicException();
            }

            var nonceB64 = nonceCandidates[0];
            var storedChunk = new Lazy<Task<string>>(
                () => Task.FromResult(nonceB64),
                LazyThreadSafetyMode.ExecutionAndPublication
            );

            _ = storedChunks.TryAdd(chunkHashB64, storedChunk);
        }

        return storedChunks;
    }

    /// <summary>
    /// Validates every entry path, chunk hash, and chunk nonce a manifest records, before any file is
    /// created, read, or overwritten.
    /// </summary>
    /// <remarks>
    /// This runs as one up-front sweep so a malformed or crafted manifest is rejected before a restore
    /// creates a directory, before a verify reads a chunk, and before an update rewrites the manifest.
    /// The decoded bytes serve only as a length check and are zeroed immediately.
    /// </remarks>
    /// <param name="manifestFiles">The manifest entries to validate.</param>
    /// <exception cref="InvalidDataException">An entry path is empty, rooted, or contains traversal or invalid characters.</exception>
    /// <exception cref="CryptographicException">A chunk hash or nonce is not Base64 of the expected length.</exception>
    private static void ValidateManifestEntries(
        IReadOnlyList<ChunkManifestFileEntry> manifestFiles
    )
    {
        foreach (var file in manifestFiles)
        {
            ManifestPathPolicy.ValidateRelative(file.OriginalPath);

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
            }
        }
    }

    /// <summary>
    /// Groups the distinct nonces a manifest records for each chunk hash.
    /// </summary>
    /// <remarks>
    /// The entries are expected to have passed <see cref="ValidateManifestEntries"/> already; the
    /// Base64 forms are what the caller keys and returns.
    /// </remarks>
    /// <param name="manifestFiles">The manifest entries to scan.</param>
    /// <returns>A dictionary mapping each Base64 chunk hash to its distinct Base64 nonces.</returns>
    private static Dictionary<string, string[]> BuildChunkNonceCandidates(
        IReadOnlyList<ChunkManifestFileEntry> manifestFiles
    )
    {
        Dictionary<string, List<string>> chunkNonceCandidates = new(StringComparer.Ordinal);

        foreach (var file in manifestFiles)
        {
            foreach (var chunk in file.Chunks)
            {
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

    /// <summary>
    /// Awaits the store operation for a chunk and returns its nonce, evicting a failed operation from
    /// the cache so a later file re-attempts it instead of reusing a permanently faulted task.
    /// </summary>
    /// <remarks>
    /// Only the operation this caller published is evicted, identified by reference, so an entry a
    /// concurrent caller won the race with is never removed. The failure is always rethrown.
    /// </remarks>
    /// <param name="chunkHashB64">The Base64-encoded content hash keying the chunk in the cache.</param>
    /// <param name="storedChunk">The operation actually held in the cache, which may belong to another caller.</param>
    /// <param name="candidateChunk">The operation this caller offered when adding the entry.</param>
    /// <param name="storedChunks">The shared cache of chunk store operations.</param>
    /// <returns>The Base64-encoded nonce the chunk was stored with.</returns>
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
                _ = storedChunks.TryRemove(
                    new KeyValuePair<string, Lazy<Task<string>>>(chunkHashB64, candidateChunk)
                );
            }

            throw;
        }
    }

    /// <summary>
    /// Orders manifest entries by path and rewrites every chunk reference with the nonce the stored
    /// chunk actually authenticates under.
    /// </summary>
    /// <remarks>
    /// An update mixes entries carried over from the previous manifest with entries produced by this
    /// run, so every chunk reference is re-emitted from the shared resolution cache and the entries are
    /// ordered by path, keeping the written manifest identical for identical content.
    /// </remarks>
    /// <param name="entries">The entries collected for the new manifest.</param>
    /// <param name="storedChunks">The cache resolving a chunk hash to its effective nonce.</param>
    /// <returns>The entries ordered by path, each carrying the resolved chunk nonces.</returns>
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

    /// <summary>
    /// Deletes the chunk files in the chunks directory that the newly saved manifest no longer
    /// references.
    /// </summary>
    /// <remarks>
    /// Pruning is best-effort cleanup that runs only after the manifest has been written, so nothing
    /// it does can lose data: a chunk that cannot be removed because it is locked or access is denied
    /// is left behind as a harmless orphan, and any other failure is swallowed rather than failing an
    /// update that has already completed.
    /// </remarks>
    /// <param name="chunksDir">The directory holding the stored chunk files.</param>
    /// <param name="referencedChunkHashes">The Base64 chunk hashes the new manifest still references.</param>
    /// <param name="namingKey">The sub-key each chunk's on-disk file name is derived from.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the unreferenced chunk files have been pruned.</returns>
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
                    _ = expectedFileNames.Add(fileName);
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
                    }
                }
            }
        }
        catch
        {
        }
    }

    /// <summary>
    /// Enumerates every file under a source directory, sorted with the platform's path comparison so
    /// runs over the same tree process files in a repeatable order.
    /// </summary>
    /// <param name="sourcePath">The directory to enumerate.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The sorted file paths and their root, or <see langword="null"/> if the directory does not exist.</returns>
    /// <exception cref="ArgumentException"><paramref name="sourcePath"/> is <see langword="null"/> or whitespace.</exception>
    private async Task<(string[] SourceFiles, string SourceRoot)?> ResolveSourceAsync(
        string sourcePath,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        if (!fileOperationsService.DirectoryExists(sourcePath))
        {
            return null;
        }

        var sourceFiles = await fileOperationsService
            .GetFilesAsync(sourcePath, "*", cancellationToken)
            .ConfigureAwait(false);

        Array.Sort(sourceFiles, StringComparer.FromComparison(PathComparer));
        return (sourceFiles, sourcePath);
    }

    /// <summary>
    /// Resolves the compression strategy for a mode, treating <see cref="CompressionMode.None"/> as
    /// no strategy so chunks bypass the compression path entirely.
    /// </summary>
    /// <param name="compressionMode">The compression mode recorded for the backup.</param>
    /// <returns>The compression strategy, or <see langword="null"/> when compression is disabled.</returns>
    private ICompressionStrategy? CreateCompressionStrategy(CompressionMode compressionMode)
    {
        return compressionMode == CompressionMode.None
            ? null
            : compressionServiceFactory.Create(compressionMode);
    }

    /// <summary>
    /// Derives the master key from the password and salt, then expands it into the purpose-bound
    /// sub-keys used for chunk encryption, nonce derivation, chunk naming, and the manifest.
    /// </summary>
    /// <remarks>
    /// The master key is zeroed if sub-key expansion fails, so no key material survives an error.
    /// </remarks>
    /// <param name="password">The user's password.</param>
    /// <param name="salt">The master salt generated for or read from the backup.</param>
    /// <param name="kdf">The key derivation algorithm recorded in the manifest preamble.</param>
    /// <returns>The derived key set; disposing it wipes every key it holds.</returns>
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

    /// <summary>
    /// Expands a 256-bit sub-key from the master key with HKDF-Expand over SHA-256.
    /// </summary>
    /// <remarks>
    /// Only the expand step is applied because the master key is already a full-length output of a
    /// password-based KDF; the context label is what keeps each sub-key bound to one purpose and
    /// independent of the others.
    /// </remarks>
    /// <param name="masterKey">The master key derived from the password.</param>
    /// <param name="context">The label identifying the sub-key's purpose.</param>
    /// <returns>The derived sub-key.</returns>
    private static byte[] DeriveSubKey(byte[] masterKey, ReadOnlySpan<byte> context)
    {
        var subKey = new byte[KeySizeBytes];
        HKDF.Expand(HashAlgorithmName.SHA256, masterKey, subKey, context);
        return subKey;
    }

    /// <summary>
    /// Wraps a block of memory in a non-writable stream, reusing the backing array when the memory
    /// exposes one so chunk data is not copied.
    /// </summary>
    /// <param name="data">The bytes to expose as a stream.</param>
    /// <returns>A read-only stream over the data.</returns>
    private static MemoryStream CreateReadOnlyStream(ReadOnlyMemory<byte> data)
    {
        return System.Runtime.InteropServices.MemoryMarshal.TryGetArray(data, out var segment)
            ? new MemoryStream(segment.Array!, segment.Offset, segment.Count, writable: false)
            : new MemoryStream(data.ToArray(), writable: false);
    }

    /// <summary>
    /// Generates the random master salt that seeds key derivation for a new backup.
    /// </summary>
    /// <returns>A fresh salt of <see cref="EncryptionConstants.SaltSize"/> bytes.</returns>
    private static byte[] GenerateSalt()
    {
        var salt = new byte[EncryptionConstants.SaltSize];
        RandomNumberGenerator.Fill(salt);
        return salt;
    }

    /// <summary>
    /// Computes a chunk's on-disk name as the lowercase hex of <c>HMAC-SHA256(namingKey, chunkHash)</c>.
    /// </summary>
    /// <remarks>
    /// Keying the name keeps the content hashes off disk, so someone reading the chunks directory
    /// cannot test whether a known file is part of the backup. The intermediate HMAC is zeroed.
    /// </remarks>
    /// <param name="namingKey">The chunk-naming sub-key.</param>
    /// <param name="chunkHash">The SHA-256 content hash of the chunk.</param>
    /// <returns>The chunk file name without its extension.</returns>
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

                if (read == 0)
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

    /// <summary>
    /// Adds up the sizes of the files about to be backed up to seed the progress total.
    /// </summary>
    /// <remarks>
    /// The total only feeds progress reporting, so a file whose size cannot be read (removed, locked,
    /// or overflowing the running sum) is skipped rather than failing the backup.
    /// </remarks>
    /// <param name="files">The file paths to measure.</param>
    /// <returns>The total size in bytes of the files that could be measured.</returns>
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
            }
        }

        return total;
    }

    /// <summary>
    /// Decodes a Base64 value read from the manifest and enforces its exact decoded length.
    /// </summary>
    /// <remarks>
    /// Length is checked before the bytes reach any cryptographic primitive, and a buffer of the
    /// wrong size is zeroed before the failure is raised.
    /// </remarks>
    /// <param name="value">The Base64 text to decode.</param>
    /// <param name="expectedLength">The exact number of bytes the value must decode to.</param>
    /// <param name="errorMessage">The message carried by the exception when validation fails.</param>
    /// <returns>The decoded bytes.</returns>
    /// <exception cref="CryptographicException">The value is not valid Base64 or decodes to the wrong length.</exception>
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

    /// <summary>
    /// Determines whether a failure is confined to the file being processed, in which case the run
    /// records the error and moves on instead of aborting the whole operation.
    /// </summary>
    /// <param name="ex">The exception thrown while processing a file.</param>
    /// <returns><see langword="true"/> if the failure affects only one file; otherwise <see langword="false"/>.</returns>
    private static bool IsFileLevelError(Exception ex)
    {
        return ex
            is FileNotFoundException
                or DirectoryNotFoundException
                or PathTooLongException
                or IOException
                or UnauthorizedAccessException;
    }

    /// <summary>
    /// Owns the master key and the purpose-bound sub-keys for the duration of a single operation, so
    /// that disposing the set wipes all of them together.
    /// </summary>
    /// <param name="masterKey">The key derived from the password and the master salt.</param>
    /// <param name="chunkEncryptionKey">The sub-key chunk contents are encrypted with.</param>
    /// <param name="chunkNonceKey">The sub-key per-chunk nonces are derived from.</param>
    /// <param name="namingKey">The sub-key chunk file names are derived from.</param>
    /// <param name="manifestEncryptionKey">The sub-key the manifest is encrypted with.</param>
    private sealed class DerivedKeySet(
        byte[] masterKey,
        byte[] chunkEncryptionKey,
        byte[] chunkNonceKey,
        byte[] namingKey,
        byte[] manifestEncryptionKey
        ) : IDisposable
    {
        /// <summary>
        /// Gets the password-derived key every sub-key is expanded from.
        /// </summary>
        public byte[] MasterKey { get; } = masterKey;

        /// <summary>
        /// Gets the sub-key used to encrypt and decrypt chunk contents.
        /// </summary>
        public byte[] ChunkEncryptionKey { get; } = chunkEncryptionKey;

        /// <summary>
        /// Gets the sub-key each chunk's deterministic nonce is derived from.
        /// </summary>
        public byte[] ChunkNonceKey { get; } = chunkNonceKey;

        /// <summary>
        /// Gets the sub-key each chunk's on-disk file name is derived from.
        /// </summary>
        public byte[] NamingKey { get; } = namingKey;

        /// <summary>
        /// Gets the sub-key used to encrypt and decrypt the manifest.
        /// </summary>
        public byte[] ManifestEncryptionKey { get; } = manifestEncryptionKey;

        /// <summary>
        /// Wipes the master key and every sub-key from memory.
        /// </summary>
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
