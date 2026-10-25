using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;

using BackupZCrypt.Application.Algorithms;
using BackupZCrypt.Application.Common;
using BackupZCrypt.Application.FileSystem;
using BackupZCrypt.Application.Manifest;
using BackupZCrypt.Domain.Algorithms;
using BackupZCrypt.Domain.Archive;

namespace BackupZCrypt.Application.Engine;

/// <summary>
/// The write path of the backup engine: creates an archive from a source folder and updates an
/// existing one. Files are split into content-defined chunks that are deduplicated by content hash,
/// optionally compressed, and individually encrypted with per-chunk nonces, and the manifest that
/// lists them is published only once every chunk is on disk.
/// </summary>
/// <param name="compressionStrategies">The registered compression strategies, looked up by mode.</param>
/// <param name="encryptionStrategies">The registered encryption strategies, looked up by algorithm.</param>
/// <param name="keyDerivationStrategies">The registered key derivation strategies, looked up by algorithm.</param>
/// <param name="fileOperationsService">The service used to measure source files.</param>
/// <param name="directoryScanService">The service used to walk the source tree.</param>
/// <param name="fileMetadataService">The service used to read file metadata.</param>
/// <param name="manifestService">The service used to write the encrypted backup manifest.</param>
/// <param name="chunkStore">The chunk store the files are written into.</param>
/// <param name="backupOpener">The opener of the backup an update works on.</param>
internal sealed class BackupWriter(
    IStrategyRegistry<CompressionMode, ICompressionStrategy> compressionStrategies,
    IStrategyRegistry<EncryptionAlgorithm, IEncryptionAlgorithmStrategy> encryptionStrategies,
    IStrategyRegistry<KeyDerivationAlgorithm, IKeyDerivationAlgorithmStrategy> keyDerivationStrategies,
    IFileOperationsService fileOperationsService,
    IDirectoryScanService directoryScanService,
    IFileMetadataService fileMetadataService,
    IManifestService manifestService,
    ChunkStore chunkStore,
    BackupOpener backupOpener
)
{
    /// <summary>
    /// Creates a new chunked backup, processing files in parallel, deduplicating chunks by content,
    /// and persisting an encrypted manifest.
    /// </summary>
    /// <remarks>
    /// An existing backup in the destination is replaced without ever being removed first: the new
    /// chunks are written under names derived from a fresh salt, the manifest is swapped atomically
    /// only once every chunk is on disk, and only then are the previous backup's chunks pruned. A run
    /// that fails or is cancelled before that point deletes the chunks it wrote and leaves the
    /// previous backup exactly as it was.
    /// </remarks>
    /// <param name="sourcePath">The directory to back up.</param>
    /// <param name="destinationPath">The directory where the chunks directory and manifest are written.</param>
    /// <param name="password">The password the archive's keys are derived from.</param>
    /// <param name="algorithms">The cipher, key derivation function, and compression the archive is written with.</param>
    /// <param name="progress">A sink that receives incremental status updates.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A result describing the backup outcome, including any per-file errors.</returns>
    internal async Task<Result<BackupResult>> CreateAsync(
        string sourcePath,
        string destinationPath,
        string password,
        ManifestHeader algorithms,
        IProgress<BackupStatus> progress,
        CancellationToken cancellationToken
    )
    {
        var stopwatch = Stopwatch.StartNew();

        var snapshot = await SourceScanner
            .ScanAsync(directoryScanService, sourcePath, cancellationToken)
            .ConfigureAwait(false);

        if (snapshot is null)
        {
            return Result<BackupResult>.Failure(MessageCode.SourcePathNotExist);
        }

        if (snapshot.Files.Count is 0)
        {
            return Result<BackupResult>.Failure(
                [new LocalizableMessage(MessageCode.NoFilesInSourceDirectory), .. snapshot.Errors]
            );
        }

        var chunksDir = await chunkStore.PrepareChunksDirectoryAsync(destinationPath, cancellationToken)
            .ConfigureAwait(false);

        byte[]? masterSalt = null;
        using ChunkWriteContext context = new(EngineLimits.MaximumChunksInFlight);
        var committed = false;

        try
        {
            masterSalt = ArchiveKeys.GenerateMasterSalt();
            using var keys = ArchiveKeys.Derive(
                password,
                masterSalt,
                keyDerivationStrategies.Resolve(algorithms.KeyDerivationAlgorithm)
            );

            var cipher = ChunkCipherSet.For(
                keys,
                encryptionStrategies.Resolve(algorithms.EncryptionAlgorithm),
                compressionStrategies.ResolveOrNull(algorithms.Compression)
            );

            List<ChunkWorkItem> workItems = [.. snapshot.Files.Select(static f => new ChunkWorkItem(f, null))];

            ProgressTracker tracker = new(
                progress,
                stopwatch,
                workItems.Count,
                this.SumFileSizes(workItems.Select(static w => w.File.FullPath))
            );
            tracker.ReportStart();

            var run = await this.ChunkFilesAsync(
                    workItems,
                    chunksDir,
                    cipher,
                    new ConcurrentDictionary<string, Lazy<Task>>(StringComparer.Ordinal),
                    context,
                    tracker,
                    cancellationToken
                )
                .ConfigureAwait(false);

            if (run.FatalError is not null)
            {
                return Result<BackupResult>.Failure(run.FatalError);
            }

            List<LocalizableMessage> errors = [.. snapshot.Errors, .. run.Errors];

            if (run.Entries.Count is 0)
            {
                return Result<BackupResult>.Failure(
                    [new LocalizableMessage(MessageCode.AllFilesFailed), .. errors]
                );
            }

            ChunkManifestData manifestData = new(
                algorithms,
                Convert.ToBase64String(masterSalt),
                [.. run.Entries.OrderBy(static f => f.OriginalPath, StringComparer.Ordinal)],
                snapshot.EmptyDirectories
            );

            ManifestEntries.Validate(manifestData.Files);

            var manifestErrors = await manifestService
                .SaveChunkManifestAsync(
                    manifestData,
                    destinationPath,
                    keys.ManifestEncryptionKey,
                    algorithms.EncryptionAlgorithm,
                    cancellationToken
                )
                .ConfigureAwait(false);

            if (manifestErrors.Count > 0)
            {
                return Result<BackupResult>.Failure([.. manifestErrors, .. errors]);
            }

            committed = true;

            _ = await chunkStore.TryPruneBackupAsync(
                    destinationPath,
                    chunksDir,
                    manifestData.Files.SelectMany(static f => f.Chunks).Select(static c => c.Hash),
                    keys.NamingKey,
                    CancellationToken.None
                )
                .ConfigureAwait(false);

            stopwatch.Stop();

            return Result<BackupResult>.Success(
                new BackupResult(
                    stopwatch.Elapsed,
                    run.Entries.Sum(static e => e.TotalSize),
                    run.Entries.Count,
                    workItems.Count + snapshot.Collisions.Count + snapshot.UnsupportedNames.Count,
                    errors,
                    snapshot.LinksWarning is { } linksWarning ? [linksWarning] : null
                )
            );
        }
        finally
        {
            if (!committed)
            {
                chunkStore.DeleteWrittenChunks(context, chunksDir);
            }

            if (masterSalt is not null)
            {
                CryptographicOperations.ZeroMemory(masterSalt);
            }
        }
    }

    /// <summary>
    /// Updates an existing chunked backup by re-chunking only the files that are new or whose size or
    /// modification time changed, rewriting the manifest, and pruning chunks no longer referenced once
    /// the manifest is saved. The encryption, key derivation, and compression settings are taken from
    /// the existing backup.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An update reuses the algorithms the archive was written with, which is why it takes none: a
    /// different key derivation function produces a different master key and the archive stops
    /// opening.
    /// </para>
    /// <para>
    /// A file whose size and modification time match the manifest, and whose chunks are all present,
    /// is carried over without being read. Every other file is chunked again; chunks the backup
    /// already holds deduplicate against it, so an unchanged file whose time merely moved writes
    /// nothing. A file that fails to read keeps its previous entry, and so does every recorded file
    /// below a folder that could not be read, so a transient access problem never removes data from
    /// the backup.
    /// </para>
    /// </remarks>
    /// <param name="sourcePath">The source directory whose current state is compared against the backup.</param>
    /// <param name="destinationPath">The directory containing the existing backup to update.</param>
    /// <param name="password">The password the archive's keys are derived from.</param>
    /// <param name="progress">A sink that receives incremental status updates.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A result describing the update outcome, including any per-file errors.</returns>
    internal async Task<Result<BackupResult>> UpdateAsync(
        string sourcePath,
        string destinationPath,
        string password,
        IProgress<BackupStatus> progress,
        CancellationToken cancellationToken
    )
    {
        var stopwatch = Stopwatch.StartNew();

        var (backup, openError) = await backupOpener.OpenAsync(
                destinationPath,
                password,
                MessageCode.ManifestRequiredForUpdate,
                MessageCode.InvalidPassword,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (backup is null)
        {
            return Result<BackupResult>.Failure(openError!);
        }

        using (backup)
        {
            return await this.UpdateOpenedBackupAsync(
                    sourcePath,
                    destinationPath,
                    backup,
                    progress,
                    stopwatch,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Runs an update against a backup whose manifest has already been opened.
    /// </summary>
    /// <param name="sourcePath">The source directory whose current state is compared against the backup.</param>
    /// <param name="destinationPath">The directory containing the existing backup to update.</param>
    /// <param name="backup">The opened backup, whose keys stay owned by the caller.</param>
    /// <param name="progress">A sink that receives incremental status updates.</param>
    /// <param name="stopwatch">The running timer of the operation.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A result describing the update outcome, including any per-file errors.</returns>
    private async Task<Result<BackupResult>> UpdateOpenedBackupAsync(
        string sourcePath,
        string destinationPath,
        OpenedBackup backup,
        IProgress<BackupStatus> progress,
        Stopwatch stopwatch,
        CancellationToken cancellationToken
    )
    {
        var snapshot = await SourceScanner
            .ScanAsync(directoryScanService, sourcePath, cancellationToken)
            .ConfigureAwait(false);

        if (snapshot is null)
        {
            return Result<BackupResult>.Failure(MessageCode.SourcePathNotExist);
        }

        var existingManifest = backup.Manifest;
        var keys = backup.Keys;
        var cipher = ChunkCipherSet.For(
            keys,
            encryptionStrategies.Resolve(backup.Preamble.Algorithm),
            compressionStrategies.ResolveOrNull(existingManifest.Header.Compression)
        );

        var chunksDir = await chunkStore.PrepareChunksDirectoryAsync(destinationPath, cancellationToken)
            .ConfigureAwait(false);

        ManifestEntries.Validate(existingManifest.Files);

        Dictionary<string, ChunkManifestFileEntry> existingFileIndex = new(StringComparer.Ordinal);
        foreach (var entry in existingManifest.Files)
        {
            existingFileIndex[entry.OriginalPath] = entry;
        }

        var storedChunks = ChunkStore.BuildStoredChunkCache(existingManifest.Files);
        chunkStore.RemoveUnavailableStoredChunks(
            storedChunks,
            existingManifest.Files,
            chunksDir,
            cipher
        );

        var plan = this.PlanUpdate(snapshot, existingFileIndex, storedChunks);

        using ChunkWriteContext context = new(EngineLimits.MaximumChunksInFlight);
        var committed = false;

        try
        {
            ProgressTracker tracker = new(
                progress,
                stopwatch,
                plan.WorkItems.Count,
                plan.WorkItems.Sum(static w => w.Size)
            );
            tracker.ReportStart();

            var run = await this.ChunkFilesAsync(
                    plan.WorkItems,
                    chunksDir,
                    cipher,
                    storedChunks,
                    context,
                    tracker,
                    cancellationToken
                )
                .ConfigureAwait(false);

            if (run.FatalError is not null)
            {
                return Result<BackupResult>.Failure(run.FatalError);
            }

            List<ChunkManifestFileEntry> entries =
            [
                .. plan.CarriedOver
                    .Concat(run.Entries)
                    .Concat(
                        run.FailedItems
                            .Where(static item => item.PreviousEntry is not null)
                            .Select(static item => item.PreviousEntry!)
                    )
                    .OrderBy(static e => e.OriginalPath, StringComparer.Ordinal),
            ];

            ChunkManifestData newManifest = new(
                new ManifestHeader(
                    backup.Preamble.Algorithm,
                    backup.Preamble.KeyDerivation,
                    existingManifest.Header.Compression
                ),
                existingManifest.MasterSalt,
                entries,
                MergeDirectories(snapshot, existingManifest.Directories, entries)
            );
            ManifestEntries.Validate(newManifest.Files);

            var manifestErrors = await manifestService
                .SaveChunkManifestAsync(
                    newManifest,
                    destinationPath,
                    keys.ManifestEncryptionKey,
                    backup.Preamble.Algorithm,
                    cancellationToken
                )
                .ConfigureAwait(false);

            List<LocalizableMessage> errors = [.. snapshot.Errors, .. run.Errors];

            if (manifestErrors.Count > 0)
            {
                return Result<BackupResult>.Failure([.. manifestErrors, .. errors]);
            }

            committed = true;

            _ = await chunkStore.TryPruneBackupAsync(
                    destinationPath,
                    chunksDir,
                    entries.SelectMany(static f => f.Chunks).Select(static c => c.Hash),
                    keys.NamingKey,
                    CancellationToken.None
                )
                .ConfigureAwait(false);

            stopwatch.Stop();

            return Result<BackupResult>.Success(
                new BackupResult(
                    stopwatch.Elapsed,
                    run.Entries.Sum(static e => e.TotalSize),
                    run.Entries.Count,
                    plan.WorkItems.Count + snapshot.Collisions.Count + snapshot.UnsupportedNames.Count,
                    errors,
                    snapshot.LinksWarning is { } linksWarning ? [linksWarning] : null,
                    new UpdateSummary(plan.CarriedOver.Count, plan.RemovedCount)
                )
            );
        }
        finally
        {
            if (!committed)
            {
                chunkStore.DeleteWrittenChunks(context, chunksDir);
            }
        }
    }

    /// <summary>
    /// Splits the source into the manifest entries an update carries over unchanged and the files it
    /// has to chunk again, and counts the recorded files it removes.
    /// </summary>
    /// <param name="snapshot">The walked source.</param>
    /// <param name="existingFileIndex">The entries of the manifest being updated, keyed by canonical path.</param>
    /// <param name="storedChunks">The cache of stored chunks that are safe to reuse.</param>
    /// <returns>The update plan.</returns>
    private UpdatePlan PlanUpdate(
        SourceSnapshot snapshot,
        Dictionary<string, ChunkManifestFileEntry> existingFileIndex,
        ConcurrentDictionary<string, Lazy<Task>> storedChunks
    )
    {
        List<ChunkManifestFileEntry> carriedOver = [];
        List<ChunkWorkItem> workItems = [];
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (var file in snapshot.Files)
        {
            _ = seen.Add(file.RelativePath);
            existingFileIndex.TryGetValue(file.RelativePath, out var existing);

            FileMetadata? metadata = null;
            try
            {
                metadata = fileMetadataService.GetFileMetadata(file.FullPath);
            }
            catch (Exception exception) when (FileFailures.IsConfinedToOneFile(exception))
            {
                metadata = null;
            }

            if (
                existing is not null
                && metadata is not null
                && ManifestEntries.IsUnchanged(existing, metadata)
                && existing.Chunks.All(chunk => storedChunks.ContainsKey(chunk.Hash))
            )
            {
                carriedOver.Add(ManifestEntries.WithMetadata(existing, metadata));
                continue;
            }

            workItems.Add(new ChunkWorkItem(file, existing, metadata?.Size ?? existing?.TotalSize ?? 0));
        }

        var removedCount = 0;
        foreach (var (path, entry) in existingFileIndex)
        {
            if (seen.Contains(path))
            {
                continue;
            }

            if (snapshot.IsInsideInaccessibleDirectory(path))
            {
                carriedOver.Add(entry);
                continue;
            }

            removedCount++;
        }

        return new UpdatePlan(carriedOver, workItems, removedCount);
    }

    /// <summary>
    /// Builds the folder list of an updated manifest: the empty folders found in the source, plus the
    /// recorded ones below a folder that could not be read, minus any that now hold a file.
    /// </summary>
    /// <param name="snapshot">The walked source.</param>
    /// <param name="previousDirectories">The folders the previous manifest recorded, if any.</param>
    /// <param name="entries">The file entries of the new manifest.</param>
    /// <returns>The folders to record.</returns>
    private static List<string> MergeDirectories(
        SourceSnapshot snapshot,
        IReadOnlyList<string>? previousDirectories,
        IReadOnlyList<ChunkManifestFileEntry> entries
    )
    {
        HashSet<string> directories = new(snapshot.EmptyDirectories, StringComparer.Ordinal);
        directories.UnionWith((previousDirectories ?? []).Where(snapshot.IsInsideInaccessibleDirectory));

        directories.RemoveWhere(directory =>
            entries.Any(e => e.OriginalPath.StartsWith(directory + "/", StringComparison.Ordinal))
        );

        return [.. directories.Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// Chunks, encrypts, and stores a list of files in parallel.
    /// </summary>
    /// <remarks>
    /// A failure confined to one file is collected and the run continues; anything else latches the
    /// first fatal error and cancels the remaining workers through a token linked to the caller's,
    /// so the caller can abandon the run without touching the manifest.
    /// </remarks>
    /// <param name="workItems">The files to capture.</param>
    /// <param name="chunksDir">The directory encrypted chunk files are written into.</param>
    /// <param name="cipher">The archive's chunk sealer and naming sub-key.</param>
    /// <param name="storedChunks">The shared cache mapping a chunk hash to its in-flight or completed store operation.</param>
    /// <param name="context">The run's chunk slots and the record of chunk files it wrote.</param>
    /// <param name="tracker">The progress tracker of the run.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The entries produced, the files that failed, and the first fatal error if one occurred.</returns>
    private async Task<ChunkRunOutcome> ChunkFilesAsync(
        IReadOnlyList<ChunkWorkItem> workItems,
        string chunksDir,
        ChunkCipherSet cipher,
        ConcurrentDictionary<string, Lazy<Task>> storedChunks,
        ChunkWriteContext context,
        ProgressTracker tracker,
        CancellationToken cancellationToken
    )
    {
        var entries = new ChunkManifestFileEntry?[workItems.Count];
        var failures = new LocalizableMessage?[workItems.Count];
        LocalizableMessage? fatalError = null;

        if (workItems.Count is 0)
        {
            return new ChunkRunOutcome([], [], [], null);
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            await Parallel
                .ForEachAsync(
                    Enumerable.Range(0, workItems.Count),
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = EngineLimits.MaximumParallelFileOperations,
                        CancellationToken = linkedCts.Token,
                    },
                    async (index, token) =>
                    {
                        var item = workItems[index];

                        try
                        {
                            var metadata = fileMetadataService.GetFileMetadata(item.File.FullPath);

                            entries[index] = await chunkStore.ChunkAndEncryptFileAsync(
                                    item.File.FullPath,
                                    item.File.RelativePath,
                                    metadata,
                                    chunksDir,
                                    cipher,
                                    storedChunks,
                                    context,
                                    tracker,
                                    token
                                )
                                .ConfigureAwait(false);
                            tracker.CompleteFile();
                        }
                        catch (Exception ex)
                            when (ex is not OperationCanceledException && FileFailures.IsConfinedToOneFile(ex))
                        {
                            failures[index] = new LocalizableMessage(
                                MessageCode.FileBackupErrorFormat,
                                item.File.DisplayPath,
                                FailureReason.From(ex)
                            );
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
            return new ChunkRunOutcome([], [], [], fatalError);
        }

        List<ChunkManifestFileEntry> produced = [];
        List<LocalizableMessage> errors = [];
        List<ChunkWorkItem> failedItems = [];

        for (var index = 0; index < workItems.Count; index++)
        {
            if (entries[index] is { } entry)
            {
                produced.Add(entry);
            }
            else if (failures[index] is { } failure)
            {
                errors.Add(failure);
                failedItems.Add(workItems[index]);
            }
        }

        return new ChunkRunOutcome(produced, errors, failedItems, null);
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
            if (
                !fileOperationsService.TryGetFileSize(file, out var size)
                || total > long.MaxValue - size
            )
            {
                continue;
            }

            total += size;
        }

        return total;
    }

    /// <summary>
    /// One file a create or update has to chunk.
    /// </summary>
    /// <param name="File">The source file.</param>
    /// <param name="PreviousEntry">The file's entry in the backup being updated, or <see langword="null"/>.</param>
    /// <param name="Size">The file size used for progress reporting.</param>
    private sealed record class ChunkWorkItem(
        SourceFile File,
        ChunkManifestFileEntry? PreviousEntry,
        long Size = 0
    );

    /// <summary>
    /// What an update does with each file.
    /// </summary>
    /// <param name="CarriedOver">The entries kept without reading their files.</param>
    /// <param name="WorkItems">The files to chunk again.</param>
    /// <param name="RemovedCount">The number of recorded files the update removes.</param>
    private sealed record class UpdatePlan(
        List<ChunkManifestFileEntry> CarriedOver,
        List<ChunkWorkItem> WorkItems,
        int RemovedCount
    );

    /// <summary>
    /// The outcome of chunking a list of files.
    /// </summary>
    /// <param name="Entries">The entries produced, in work-list order.</param>
    /// <param name="Errors">The per-file errors, in work-list order.</param>
    /// <param name="FailedItems">The work items that failed.</param>
    /// <param name="FatalError">The first error that stopped the run, if any.</param>
    private sealed record class ChunkRunOutcome(
        List<ChunkManifestFileEntry> Entries,
        List<LocalizableMessage> Errors,
        List<ChunkWorkItem> FailedItems,
        LocalizableMessage? FatalError
    );
}
