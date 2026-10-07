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
using BackupZCrypt.Domain.ValueObjects.FileSystem;
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
/// path (create and update), <c>.Restore</c> the read path (restore and verify), <c>.Preview</c> the
/// read-only comparisons run before an operation, <c>.Chunks</c> the chunk store and the
/// manifest-entry plumbing both paths share, and <c>.Keys</c> the key derivation, salt, and
/// chunk-naming primitives. The split is purely a file boundary: it moves no member between types.
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
    /// The number of file pipelines run at once on this machine, as decided by
    /// <see cref="FileParallelismPolicy"/>.
    /// </summary>
    private static readonly int MaximumParallelFileOperations =
        FileParallelismPolicy.ForProcessorCount(Environment.ProcessorCount);

    /// <summary>
    /// The number of chunks compressed, encrypted, and written at once across every file of a run.
    /// Bounding it caps memory at a few chunk buffers each, while still letting a single large file
    /// keep every processor busy instead of being processed one chunk at a time.
    /// </summary>
    private static readonly int MaximumChunksInFlight =
        FileParallelismPolicy.ChunksInFlightForProcessorCount(Environment.ProcessorCount);

    /// <summary>
    /// The comparison applied to backup paths, shared with every other layer that compares them.
    /// </summary>
    private static readonly StringComparison PathComparer = PathNormalizationHelper.PathComparer;

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

        var snapshot = await SourceScanner
            .ScanAsync(fileOperationsService, sourcePath, cancellationToken)
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

        var chunksDir = await this.PrepareChunksDirectoryAsync(destinationPath, cancellationToken)
            .ConfigureAwait(false);

        byte[]? masterSalt = null;
        using ChunkWriteContext context = new(MaximumChunksInFlight);
        var committed = false;

        try
        {
            masterSalt = GenerateSalt();
            using var keys = this.DeriveKeySet(
                PasswordForNewBackup(request.Password),
                masterSalt,
                request.KeyDerivationAlgorithm
            );

            ChunkCipherSet cipher = new(
                keys.ChunkEncryptionKey,
                keys.ChunkNonceKey,
                keys.NamingKey,
                encryptionServiceFactory.Create(request.EncryptionAlgorithm),
                this.CreateCompressionStrategy(request.Compression)
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
                    new ConcurrentDictionary<string, Lazy<Task<string>>>(StringComparer.Ordinal),
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
                new ManifestHeader(
                    request.EncryptionAlgorithm,
                    request.KeyDerivationAlgorithm,
                    request.Compression
                ),
                Convert.ToBase64String(masterSalt),
                [.. run.Entries.OrderBy(static f => f.OriginalPath, StringComparer.Ordinal)],
                snapshot.EmptyDirectories
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

            if (manifestErrors.Count > 0)
            {
                return Result<BackupResult>.Failure([.. manifestErrors, .. errors]);
            }

            committed = true;

            _ = await this.TryPruneBackupAsync(
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
                this.DeleteWrittenChunks(context, chunksDir);
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
    /// An update reuses the algorithms the archive was written with, never the ones
    /// <paramref name="request"/> carries: a different key derivation function produces a different
    /// master key and the archive stops opening.
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

        var (backup, openError) = await this.OpenBackupAsync(
                destinationPath,
                request.Password,
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
            .ScanAsync(fileOperationsService, sourcePath, cancellationToken)
            .ConfigureAwait(false);

        if (snapshot is null)
        {
            return Result<BackupResult>.Failure(MessageCode.SourcePathNotExist);
        }

        var existingManifest = backup.Manifest;
        var keys = backup.Keys;
        var compressionStrategy = this.CreateCompressionStrategy(existingManifest.Header.Compression);

        ChunkCipherSet cipher = new(
            keys.ChunkEncryptionKey,
            keys.ChunkNonceKey,
            keys.NamingKey,
            encryptionServiceFactory.Create(backup.Preamble.Algorithm),
            compressionStrategy
        );

        var chunksDir = await this.PrepareChunksDirectoryAsync(destinationPath, cancellationToken)
            .ConfigureAwait(false);

        ValidateManifestEntries(existingManifest.Files);

        Dictionary<string, ChunkManifestFileEntry> existingFileIndex = new(StringComparer.Ordinal);
        foreach (var entry in existingManifest.Files)
        {
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

        var plan = this.PlanUpdate(snapshot, existingFileIndex, storedChunks);

        using ChunkWriteContext context = new(MaximumChunksInFlight);
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

            List<ChunkManifestFileEntry> entries = [.. plan.CarriedOver, .. run.Entries];
            entries.AddRange(
                run.FailedItems
                    .Where(static item => item.PreviousEntry is not null)
                    .Select(static item => item.PreviousEntry! with { OriginalPath = item.File.RelativePath })
            );

            var canonicalEntries = await CanonicalizeChunkEntriesAsync(entries, storedChunks)
                .ConfigureAwait(false);

            ChunkManifestData newManifest = new(
                new ManifestHeader(
                    backup.Preamble.Algorithm,
                    backup.Preamble.KeyDerivation,
                    existingManifest.Header.Compression
                ),
                existingManifest.MasterSalt,
                canonicalEntries,
                MergeDirectories(snapshot, existingManifest.Directories, canonicalEntries)
            );
            ValidateManifestEntries(newManifest.Files);

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

            _ = await this.TryPruneBackupAsync(
                    destinationPath,
                    chunksDir,
                    canonicalEntries.SelectMany(static f => f.Chunks).Select(static c => c.Hash),
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
                    plan.CarriedOver.Count,
                    plan.RemovedCount
                )
            );
        }
        finally
        {
            if (!committed)
            {
                this.DeleteWrittenChunks(context, chunksDir);
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
        ConcurrentDictionary<string, Lazy<Task<string>>> storedChunks
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
                metadata = fileOperationsService.GetFileMetadata(file.FullPath);
            }
            catch (Exception exception) when (IsFileLevelError(exception))
            {
                metadata = null;
            }

            if (
                existing is not null
                && metadata is not null
                && IsUnchanged(existing, metadata)
                && existing.Chunks.All(chunk => storedChunks.ContainsKey(chunk.Hash))
            )
            {
                carriedOver.Add(WithMetadata(existing with { OriginalPath = file.RelativePath }, metadata));
                continue;
            }

            workItems.Add(new ChunkWorkItem(file, existing, metadata?.Size ?? existing?.TotalSize ?? 0));
        }

        var removedCount = 0;
        foreach (var (canonicalPath, entry) in existingFileIndex)
        {
            if (seen.Contains(canonicalPath))
            {
                continue;
            }

            if (snapshot.IsInsideInaccessibleDirectory(canonicalPath))
            {
                carriedOver.Add(entry with { OriginalPath = canonicalPath });
                continue;
            }

            removedCount++;
        }

        return new UpdatePlan(carriedOver, workItems, removedCount);
    }

    /// <summary>
    /// Determines whether a recorded entry still describes the file on disk, judged by its size and
    /// modification time, the way most backup tools detect changes without reading the file.
    /// </summary>
    /// <param name="entry">The recorded manifest entry.</param>
    /// <param name="metadata">The file's current metadata.</param>
    /// <returns><see langword="true"/> when the file can be carried over without being read.</returns>
    private static bool IsUnchanged(ChunkManifestFileEntry entry, FileMetadata metadata)
    {
        return entry.LastWriteTimeUtc is { } recorded
            && entry.TotalSize == metadata.Size
            && recorded.ToUniversalTime().Ticks == metadata.LastWriteTimeUtc.ToUniversalTime().Ticks;
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

        foreach (var directory in previousDirectories ?? [])
        {
            var canonical = ManifestPathPolicy.Canonicalize(directory);
            if (snapshot.IsInsideInaccessibleDirectory(canonical))
            {
                _ = directories.Add(canonical);
            }
        }

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
    /// <param name="cipher">The key material and strategies chunks are compressed and encrypted with.</param>
    /// <param name="storedChunks">The shared cache mapping a chunk hash to its in-flight or completed store operation.</param>
    /// <param name="context">The run's chunk slots and the record of chunk files it wrote.</param>
    /// <param name="tracker">The progress tracker of the run.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The entries produced, the files that failed, and the first fatal error if one occurred.</returns>
    private async Task<ChunkRunOutcome> ChunkFilesAsync(
        IReadOnlyList<ChunkWorkItem> workItems,
        string chunksDir,
        ChunkCipherSet cipher,
        ConcurrentDictionary<string, Lazy<Task<string>>> storedChunks,
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
                        MaxDegreeOfParallelism = MaximumParallelFileOperations,
                        CancellationToken = linkedCts.Token,
                    },
                    async (index, token) =>
                    {
                        var item = workItems[index];

                        try
                        {
                            var metadata = fileOperationsService.GetFileMetadata(item.File.FullPath);

                            var entry = await this.ChunkAndEncryptFileAsync(
                                    item.File.FullPath,
                                    item.File.RelativePath,
                                    chunksDir,
                                    cipher,
                                    storedChunks,
                                    context,
                                    tracker,
                                    token
                                )
                                .ConfigureAwait(false);

                            entries[index] = WithMetadata(entry, metadata);
                            tracker.CompleteFile();
                        }
                        catch (Exception ex)
                            when (ex is not OperationCanceledException && IsFileLevelError(ex))
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
    /// Creates the backup folder and its chunks directory, refusing a chunks directory reached
    /// through a link.
    /// </summary>
    /// <param name="destinationPath">The backup folder.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The full path of the chunks directory.</returns>
    private async Task<string> PrepareChunksDirectoryAsync(
        string destinationPath,
        CancellationToken cancellationToken
    )
    {
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

        return chunksDir;
    }

    /// <summary>
    /// Deletes every chunk file a run wrote, after the run failed or was cancelled before its manifest
    /// was published, and removes the chunks directory again when that leaves it empty.
    /// </summary>
    /// <remarks>
    /// The chunks of a run are named under its own keys, so none of them can belong to the backup the
    /// run was going to replace. Cleanup is best-effort: a file that cannot be deleted stays behind as
    /// an orphan the next successful run prunes.
    /// </remarks>
    /// <param name="context">The run's record of the chunk files it wrote.</param>
    /// <param name="chunksDir">The chunks directory.</param>
    private void DeleteWrittenChunks(ChunkWriteContext context, string chunksDir)
    {
        foreach (var chunkFile in context.WrittenChunks)
        {
            _ = fileOperationsService.TryDeleteFile(chunkFile);
        }

        try
        {
            if (
                fileOperationsService.DirectoryExists(chunksDir)
                && fileOperationsService.GetDirectoryEntryNames(chunksDir).Count is 0
            )
            {
                fileOperationsService.DeleteEmptyDirectory(chunksDir);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
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
    /// Attaches the metadata read from the source file to the entry produced for it.
    /// </summary>
    /// <param name="entry">The entry produced by chunking the file.</param>
    /// <param name="metadata">The file's metadata, read before its content.</param>
    /// <returns>The entry carrying the file's modification time and attributes.</returns>
    private static ChunkManifestFileEntry WithMetadata(ChunkManifestFileEntry entry, FileMetadata metadata)
    {
        var attributes =
            (metadata.IsReadOnly ? ManifestFileAttributes.ReadOnly : ManifestFileAttributes.None)
            | (metadata.IsHidden ? ManifestFileAttributes.Hidden : ManifestFileAttributes.None);

        return entry with
        {
            LastWriteTimeUtc = DateTime.SpecifyKind(metadata.LastWriteTimeUtc, DateTimeKind.Utc),
            Attributes = attributes is ManifestFileAttributes.None ? null : attributes,
            UnixMode = metadata.UnixMode,
        };
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

    /// <summary>
    /// The resources one create or update shares between its files: the slots bounding how many chunks
    /// are processed at once, and the record of the chunk files the run wrote.
    /// </summary>
    /// <param name="maximumChunksInFlight">The number of chunks processed at once.</param>
    private sealed class ChunkWriteContext(int maximumChunksInFlight) : IDisposable
    {
        /// <summary>
        /// Gets the slots a chunk takes while it is compressed, encrypted, and written.
        /// </summary>
        public SemaphoreSlim ChunkSlots { get; } = new(maximumChunksInFlight, maximumChunksInFlight);

        /// <summary>
        /// Gets the chunk files this run created.
        /// </summary>
        public ConcurrentBag<string> WrittenChunks { get; } = [];

        /// <summary>
        /// Releases the chunk slots.
        /// </summary>
        public void Dispose()
        {
            this.ChunkSlots.Dispose();
        }
    }

    /// <summary>
    /// Reports the progress of a run as bytes are read and files complete. The processed byte count
    /// is capped at the total measured before the run, so a file that grows while it is read never
    /// produces an impossible report.
    /// </summary>
    /// <param name="progress">The sink that receives status updates.</param>
    /// <param name="stopwatch">The running timer of the operation.</param>
    /// <param name="totalFiles">The number of files in the run.</param>
    /// <param name="totalBytes">The size of the files measured before the run.</param>
    private sealed class ProgressTracker(
        IProgress<BackupStatus>? progress,
        Stopwatch stopwatch,
        int totalFiles,
        long totalBytes
    )
    {
        /// <summary>
        /// The bytes read so far.
        /// </summary>
        private long processedBytes;

        /// <summary>
        /// The files completed so far.
        /// </summary>
        private int processedFiles;

        /// <summary>
        /// Reports the empty starting status.
        /// </summary>
        public void ReportStart()
        {
            progress?.Report(new BackupStatus(0, totalFiles, 0, totalBytes, TimeSpan.Zero));
        }

        /// <summary>
        /// Records bytes read from a file.
        /// </summary>
        /// <param name="bytes">The number of bytes read.</param>
        public void AddBytes(long bytes)
        {
            var current = Interlocked.Add(ref this.processedBytes, bytes);
            this.Report(Volatile.Read(ref this.processedFiles), current);
        }

        /// <summary>
        /// Records a completed file.
        /// </summary>
        public void CompleteFile()
        {
            var files = Interlocked.Increment(ref this.processedFiles);
            this.Report(files, Volatile.Read(ref this.processedBytes));
        }

        /// <summary>
        /// Sends one status report.
        /// </summary>
        /// <param name="files">The files completed so far.</param>
        /// <param name="bytes">The bytes read so far.</param>
        private void Report(int files, long bytes)
        {
            progress?.Report(
                new BackupStatus(
                    Math.Min(files, totalFiles),
                    totalFiles,
                    Math.Clamp(bytes, 0, totalBytes),
                    totalBytes,
                    stopwatch.Elapsed
                )
            );
        }
    }
}
