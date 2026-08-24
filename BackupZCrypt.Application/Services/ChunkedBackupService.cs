using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;

using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.Utilities.Extensions;
using BackupZCrypt.Application.Utilities.Helpers;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Application.ValueObjects.Manifest;
using BackupZCrypt.Domain.Constants;
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
/// <remarks>
/// The class is split across partial files along its operation seams — this file carries the write
/// path (create and update), <c>.Restore</c> the read path (restore and verify), <c>.Chunks</c> the
/// chunk store and the manifest-entry plumbing both paths share, and <c>.Keys</c> the key
/// derivation, salt, and chunk-naming primitives. The split is purely a file boundary: it moves no
/// member between types and changes nothing that reaches disk.
/// </remarks>
/// <param name="compressionServiceFactory">The factory producing compression strategies for a compression mode.</param>
/// <param name="encryptionServiceFactory">The factory producing encryption strategies for an algorithm.</param>
/// <param name="fileOperationsService">The service used to read, write, and enumerate files.</param>
/// <param name="manifestService">The service used to read and write the encrypted backup manifest.</param>
/// <param name="chunkingStrategy">The strategy used to split file streams into content-defined chunks.</param>
/// <param name="keyDerivationServiceFactory">The factory producing key derivation services for an algorithm.</param>
internal sealed partial class ChunkedBackupService(
    ICompressionServiceFactory compressionServiceFactory,
    IEncryptionServiceFactory encryptionServiceFactory,
    IFileOperationsService fileOperationsService,
    IManifestService manifestService,
    IChunkingStrategy chunkingStrategy,
    IKeyDerivationServiceFactory keyDerivationServiceFactory
) : IChunkedBackupService
{
    /// <summary>
    /// The length in bytes of a 256-bit key.
    /// </summary>
    private const int KeySizeBytes = EncryptionConstants.KeySize / 8;

    /// <summary>
    /// The largest encrypted chunk file the reader accepts. Zstandard's bounded overhead is well
    /// below 64 KiB for a 4 MiB input; the margin rejects unbounded allocations without constraining
    /// any chunk the writer can produce.
    /// </summary>
    private const int MaximumStoredChunkSize =
        BackupConstants.MaximumChunkSize + (64 * 1024) + EncryptionConstants.TagSize;

    /// <summary>
    /// Caps simultaneous file pipelines because each worker can hold several four-megabyte plaintext,
    /// compressed, and ciphertext buffers at once.
    /// </summary>
    private static readonly int MaximumParallelFileOperations = Math.Clamp(
        Environment.ProcessorCount,
        1,
        4
    );

    /// <summary>
    /// The comparison applied to backup paths, shared with every other layer that compares them.
    /// </summary>
    private static readonly StringComparison PathComparer = PathNormalizationHelper.PathComparer;

    /// <summary>
    /// Describes one source file that an update must capture, including its safe fallback entry.
    /// </summary>
    /// <param name="File">The source file path.</param>
    /// <param name="RelativePath">The canonical manifest path.</param>
    /// <param name="Size">The source size used for progress reporting.</param>
    /// <param name="PreviousEntry">The last manifest entry, or <see langword="null"/> for a new file.</param>
    private sealed record UpdateFileWorkItem(
        string File,
        string RelativePath,
        long Size,
        ChunkManifestFileEntry? PreviousEntry
    );

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
        if (sourceFiles.Length is 0)
        {
            stopwatch.Stop();
            return Result<BackupResult>.Success(
                new BackupResult(
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

        ManifestPathPolicy.EnsureNoReparsePointDescendants(
            fileOperationsService,
            destinationPath,
            chunksDir
        );
        await fileOperationsService
            .CreateDirectoryAsync(chunksDir, cancellationToken)
            .ConfigureAwait(false);
        ManifestPathPolicy.EnsureNoReparsePointDescendants(
            fileOperationsService,
            destinationPath,
            chunksDir
        );

        byte[]? masterSalt = null;
        DerivedKeySet? keys = null;

        try
        {
            masterSalt = GenerateSalt();
            keys = DeriveKeySet(request.Password, masterSalt, request.KeyDerivationAlgorithm);

            var encryptionStrategy = encryptionServiceFactory.Create(request.EncryptionAlgorithm);
            var compressionStrategy = CreateCompressionStrategy(request.Compression);

            ChunkCipherSet cipher = new(
                keys.ChunkEncryptionKey,
                keys.ChunkNonceKey,
                keys.NamingKey,
                encryptionStrategy,
                compressionStrategy
            );

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
            var maxDop = MaximumParallelFileOperations;

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
                                        chunksDir,
                                        cipher,
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
                            catch (Exception ex)
                                when (ex is not OperationCanceledException && IsFileLevelError(ex))
                            {
                                errors.Add(
                                    new LocalizableMessage(
                                        MessageCode.EncryptionErrorFormat,
                                        file,
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

            ValidateManifestEntries(manifestData.Files);

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

            if (manifestErrors.Count > 0)
            {
                return Result<BackupResult>.Failure([.. errorList]);
            }

            return processedFiles is 0
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

            ChunkCipherSet cipher = new(
                keys.ChunkEncryptionKey,
                keys.ChunkNonceKey,
                keys.NamingKey,
                encryptionStrategy,
                compressionStrategy
            );

            var chunksDir = fileOperationsService.CombinePath(
                destinationPath,
                BackupConstants.ChunksDirectoryName
            );

            ManifestPathPolicy.EnsureNoReparsePointDescendants(
                fileOperationsService,
                destinationPath,
                chunksDir
            );
            await fileOperationsService
                .CreateDirectoryAsync(chunksDir, cancellationToken)
                .ConfigureAwait(false);
            ManifestPathPolicy.EnsureNoReparsePointDescendants(
                fileOperationsService,
                destinationPath,
                chunksDir
            );

            ValidateManifestEntries(existingManifest.Files);

            Dictionary<string, ChunkManifestFileEntry> existingFileIndex = new(
                StringComparer.FromComparison(PathComparer)
            );
            foreach (var entry in existingManifest.Files)
            {
                ManifestPathPolicy.ValidateRelative(entry.OriginalPath);
                existingFileIndex[ManifestPathPolicy.Canonicalize(entry.OriginalPath)] = entry;
            }

            var storedChunks = BuildStoredChunkNonceCache(existingManifest.Files);
            this.RemoveUnavailableStoredChunks(
                storedChunks,
                existingManifest.Files,
                chunksDir,
                keys.NamingKey,
                compressionStrategy
            );

            ConcurrentBag<ChunkManifestFileEntry> updatedEntries = [];
            ConcurrentDictionary<string, byte> referencedChunkHashes = new(StringComparer.Ordinal);

            var filesToProcess = await PartitionUpdateFilesAsync(
                    sourceFiles,
                    sourceRoot,
                    existingFileIndex,
                    storedChunks,
                    updatedEntries,
                    referencedChunkHashes,
                    cancellationToken
                )
                .ConfigureAwait(false);

            var totalFilesToProcess = filesToProcess.Count;
            var totalBytes = filesToProcess.Sum(static f => f.Size);

            progress?.Report(
                new BackupStatus(0, totalFilesToProcess, 0, totalBytes, TimeSpan.Zero)
            );


            var (processedFiles, errors, fatalError) = await ChunkUpdatedFilesAsync(
                    filesToProcess,
                    chunksDir,
                    cipher,
                    storedChunks,
                    updatedEntries,
                    referencedChunkHashes,
                    totalBytes,
                    progress,
                    stopwatch,
                    cancellationToken
                )
                .ConfigureAwait(false);

            if (fatalError is not null)
            {
                stopwatch.Stop();
                return Result<BackupResult>.Failure(fatalError);
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
            ValidateManifestEntries(newManifest.Files);

            var manifestErrors = await manifestService
                .SaveChunkManifestAsync(
                    newManifest,
                    destinationPath,
                    keys.ManifestEncryptionKey,
                    preamble.Algorithm,
                    cancellationToken
                )
                .ConfigureAwait(false);

            List<LocalizableMessage> errorList = [.. errors];
            errorList.AddRange(manifestErrors);

            if (manifestErrors.Count > 0)
            {
                stopwatch.Stop();
                return Result<BackupResult>.Failure([.. errorList]);
            }

            _ = await TryDeleteOrphanedChunksAsync(
                    chunksDir,
                    referencedChunkHashes.Keys,
                    keys.NamingKey,
                    cancellationToken
                )
                .ConfigureAwait(false);

            stopwatch.Stop();

            return Result<BackupResult>.Success(
                new BackupResult(
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
    /// Splits the files currently under the source root into the manifest entries that can be
    /// carried over unchanged and the files that have to be re-chunked.
    /// </summary>
    /// <remarks>
    /// A file the manifest already knows, whose SHA-256 still matches, and whose stored chunks remain
    /// usable is reused verbatim. Its chunks are recorded as still in use so pruning does not delete
    /// them. Every other file — added, modified, absent, or missing a usable stored chunk — is
    /// queued for chunking. The files are visited in the order the caller supplies them, so the
    /// queue and the carried-over entries keep that order.
    /// </remarks>
    /// <param name="sourceFiles">The files currently present under the source root.</param>
    /// <param name="sourceRoot">The root the manifest paths are relative to.</param>
    /// <param name="existingFileIndex">The entries of the manifest being updated, keyed by manifest path.</param>
    /// <param name="storedChunks">The filtered cache of chunks safe to reuse.</param>
    /// <param name="unchangedEntries">The collector the carried-over manifest entries are added to.</param>
    /// <param name="referencedChunkHashes">The set of chunk hashes the new manifest still references.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// The files that have to be chunked, with their manifest path and previous entry when one exists.
    /// </returns>
    /// <exception cref="InvalidDataException">A file's manifest path is empty, rooted, or contains traversal.</exception>
    private async Task<List<UpdateFileWorkItem>> PartitionUpdateFilesAsync(
        string[] sourceFiles,
        string sourceRoot,
        Dictionary<string, ChunkManifestFileEntry> existingFileIndex,
        ConcurrentDictionary<string, Lazy<Task<string>>> storedChunks,
        ConcurrentBag<ChunkManifestFileEntry> unchangedEntries,
        ConcurrentDictionary<string, byte> referencedChunkHashes,
        CancellationToken cancellationToken
    )
    {
        List<UpdateFileWorkItem> filesToProcess = [];

        foreach (var file in sourceFiles)
        {
            var relativePath = ManifestPathPolicy.ToManifestPath(
                fileOperationsService.GetRelativePath(sourceRoot, file)
            );
            ManifestPathPolicy.ValidateRelative(relativePath);
            var fileSize = fileOperationsService.GetFileSize(file);

            if (!existingFileIndex.TryGetValue(relativePath, out var existing))
            {
                filesToProcess.Add(new UpdateFileWorkItem(file, relativePath, fileSize, null));
                continue;
            }

            var currentHash = await fileOperationsService
                .ComputeFileHashAsync(file, cancellationToken)
                .ConfigureAwait(false);

            if (
                !string.Equals(currentHash, existing.FileHash, StringComparison.Ordinal)
                || existing.Chunks.Any(chunk => !storedChunks.ContainsKey(chunk.Hash))
            )
            {
                filesToProcess.Add(new UpdateFileWorkItem(file, relativePath, fileSize, existing));
                continue;
            }

            unchangedEntries.Add(existing with { OriginalPath = relativePath });

            foreach (var chunk in existing.Chunks)
            {
                _ = referencedChunkHashes.TryAdd(chunk.Hash, 0);
            }
        }

        return filesToProcess;
    }

    /// <summary>
    /// Chunks, encrypts, and stores the files an update has to rewrite, in parallel.
    /// </summary>
    /// <remarks>
    /// A failure confined to one file is collected and the run continues; anything else latches the
    /// first fatal error and cancels the remaining workers through a token linked to the caller's,
    /// so the caller can abandon the update without rewriting the manifest. An empty work list is
    /// answered without creating a linked token source or starting a parallel loop at all.
    /// </remarks>
    /// <param name="files">The source files to capture and their optional previous entries.</param>
    /// <param name="chunksDir">The directory encrypted chunk files are written into.</param>
    /// <param name="cipher">The key material and strategies chunks are compressed and encrypted with.</param>
    /// <param name="storedChunks">The shared cache mapping a chunk hash to its in-flight or completed store operation.</param>
    /// <param name="updatedEntries">The collector the manifest entries produced here are added to.</param>
    /// <param name="referencedChunkHashes">The set of chunk hashes the new manifest references.</param>
    /// <param name="totalBytes">The size of the work list, used only for progress reporting.</param>
    /// <param name="progress">A sink that receives incremental status updates.</param>
    /// <param name="stopwatch">The running timer whose elapsed time is reported with each update.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>How many files were stored, the per-file errors, and the first fatal error if one occurred.</returns>
    private async Task<(
        int ProcessedFiles,
        ConcurrentBag<LocalizableMessage> Errors,
        LocalizableMessage? FatalError
    )> ChunkUpdatedFilesAsync(
        List<UpdateFileWorkItem> files,
        string chunksDir,
        ChunkCipherSet cipher,
        ConcurrentDictionary<string, Lazy<Task<string>>> storedChunks,
        ConcurrentBag<ChunkManifestFileEntry> updatedEntries,
        ConcurrentDictionary<string, byte> referencedChunkHashes,
        long totalBytes,
        IProgress<BackupStatus>? progress,
        Stopwatch stopwatch,
        CancellationToken cancellationToken
    )
    {
        ConcurrentBag<LocalizableMessage> errors = [];
        var processedFiles = 0;
        var totalFilesToProcess = files.Count;

        if (totalFilesToProcess is 0)
        {
            return (processedFiles, errors, null);
        }

        long processedBytes = 0;
        LocalizableMessage? fatalError = null;

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            await Parallel
                .ForEachAsync(
                    files,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = MaximumParallelFileOperations,
                        CancellationToken = linkedCts.Token,
                    },
                    async (fileItem, token) =>
                    {
                        try
                        {
                            var entry = await ChunkAndEncryptFileAsync(
                                    fileItem.File,
                                    fileItem.RelativePath,
                                    chunksDir,
                                    cipher,
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
                            var currentBytes = Interlocked.Add(ref processedBytes, fileItem.Size);

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
                        catch (Exception ex)
                            when (ex is not OperationCanceledException && IsFileLevelError(ex))
                        {
                            errors.Add(
                                new LocalizableMessage(
                                    MessageCode.EncryptionErrorFormat,
                                    fileItem.File,
                                    ex.Message
                                )
                            );

                            if (fileItem.PreviousEntry is not null)
                            {
                                updatedEntries.Add(
                                    fileItem.PreviousEntry with
                                    {
                                        OriginalPath = fileItem.RelativePath,
                                    }
                                );

                                foreach (var chunk in fileItem.PreviousEntry.Chunks)
                                {
                                    _ = referencedChunkHashes.TryAdd(chunk.Hash, 0);
                                }
                            }
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            _ = Interlocked.CompareExchange(
                                ref fatalError,
                                new LocalizableMessage(MessageCode.UnexpectedErrorFormat, ex.Message),
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
            return (processedFiles, errors, fatalError);
        }

        return (processedFiles, errors, null);
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
            if (fileOperationsService.TryGetFileSize(file, out var size) && total <= long.MaxValue - size)
            {
                total += size;
            }
        }

        return total;
    }
}
