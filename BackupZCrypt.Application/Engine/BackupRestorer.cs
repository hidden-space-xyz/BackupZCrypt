using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;

using BackupZCrypt.Application.Backup;
using BackupZCrypt.Application.Common;
using BackupZCrypt.Domain.Archive;
using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Services.Interfaces;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Backup;
using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Application.Engine;

/// <summary>
/// The read path of the backup engine: restores an archive into a folder and verifies it without
/// writing anything, decrypting and reassembling chunks in parallel and checking every file against
/// its manifest entry.
/// </summary>
/// <param name="compressionStrategies">The registered compression strategies, looked up by mode.</param>
/// <param name="encryptionStrategies">The registered encryption strategies, looked up by algorithm.</param>
/// <param name="fileOperationsService">The service used to read chunks and write restored files.</param>
/// <param name="fileMetadataService">The service used to restore file metadata.</param>
/// <param name="backupOpener">The opener of the backup being read.</param>
internal sealed class BackupRestorer(
    IStrategyRegistry<CompressionMode, ICompressionStrategy> compressionStrategies,
    IStrategyRegistry<EncryptionAlgorithm, IEncryptionAlgorithmStrategy> encryptionStrategies,
    IFileOperationsService fileOperationsService,
    IFileMetadataService fileMetadataService,
    BackupOpener backupOpener
)
{
    /// <summary>
    /// Restores files from a chunked backup, decrypting and reassembling chunks in parallel and
    /// verifying each restored file's size and hash against the manifest.
    /// </summary>
    /// <remarks>
    /// The destination folder is created only once the manifest has been opened, so a wrong password
    /// leaves nothing behind. Each entry is checked against the running system before anything is
    /// written: a name the system cannot create, a name that clashes with another restored file once
    /// letter case is ignored, or a target inside the backup folder itself is reported for that file
    /// while every other file is still restored.
    /// </remarks>
    /// <param name="sourcePath">The directory containing the backup chunks and manifest.</param>
    /// <param name="destinationPath">The directory into which files are reconstructed.</param>
    /// <param name="request">The backup request carrying the password used to decrypt the manifest.</param>
    /// <param name="progress">A sink that receives incremental status updates.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A result describing the restore outcome; a wrong password surfaces as a failure result.</returns>
    internal async Task<Result<BackupResult>> RestoreAsync(
        string sourcePath,
        string destinationPath,
        RestoreBackupRequest request,
        IProgress<BackupStatus> progress,
        CancellationToken cancellationToken
    )
    {
        var stopwatch = Stopwatch.StartNew();

        var (backup, openError) = await backupOpener.OpenAsync(
                sourcePath,
                request.Password,
                MessageCode.ManifestRequiredForDecryption,
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
            var manifest = backup.Manifest;
            var reader = this.CreateChunkReader(backup, sourcePath);

            ManifestEntries.Validate(manifest.Files);

            ConcurrentBag<LocalizableMessage> errors = [];
            var plan = PlanRestore(manifest, sourcePath, destinationPath, errors);

            await fileOperationsService
                .CreateDirectoryAsync(destinationPath, cancellationToken)
                .ConfigureAwait(false);

            await this.RestoreDirectoriesAsync(
                    manifest.Directories,
                    sourcePath,
                    destinationPath,
                    errors,
                    cancellationToken
                )
                .ConfigureAwait(false);

            var totalFiles = manifest.Files.Count;
            ProgressTracker tracker = new(
                progress,
                stopwatch,
                totalFiles,
                plan.Sum(static item => item.Entry.TotalSize)
            );
            tracker.ReportStart();

            long restoredBytes = 0;
            var processedFiles = 0;
            LocalizableMessage? fatalError = null;

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                await Parallel
                    .ForEachAsync(
                        plan,
                        new ParallelOptions
                        {
                            MaxDegreeOfParallelism = EngineLimits.MaximumParallelFileOperations,
                            CancellationToken = linkedCts.Token,
                        },
                        async (item, token) =>
                        {
                            try
                            {
                                await this.RestoreFileFromChunksAsync(
                                        item.Entry,
                                        item.TargetPath,
                                        destinationPath,
                                        reader,
                                        tracker,
                                        token
                                    )
                                    .ConfigureAwait(false);

                                _ = Interlocked.Increment(ref processedFiles);
                                _ = Interlocked.Add(ref restoredBytes, item.Entry.TotalSize);
                                tracker.CompleteFile();
                            }
                            catch (Exception ex)
                                when (ex is not OperationCanceledException
                                    && (ex is CryptographicException || FileFailures.IsConfinedToOneFile(ex)))
                            {
                                errors.Add(
                                    new LocalizableMessage(
                                        MessageCode.DecryptionErrorFormat,
                                        item.Entry.OriginalPath,
                                        FailureReason.From(ex)
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
                return Result<BackupResult>.Failure(fatalError);
            }

            List<LocalizableMessage> errorList = OrderMessages(errors);
            stopwatch.Stop();

            return errorList.Count > 0 && processedFiles is 0
                ? Result<BackupResult>.Failure(
                    [new LocalizableMessage(MessageCode.AllFilesFailed), .. errorList]
                )
                : Result<BackupResult>.Success(
                    new BackupResult(
                        stopwatch.Elapsed,
                        restoredBytes,
                        processedFiles,
                        totalFiles,
                        errors: errorList
                    )
                );
        }
    }

    /// <summary>
    /// Verifies the integrity of a chunked backup without restoring any file. After the manifest is
    /// decrypted (a wrong password surfaces as a failure), every file is processed in parallel: its
    /// chunks are decrypted, authenticated, decompressed, and re-hashed against the manifest, with
    /// the reconstructed bytes discarded to <see cref="Stream.Null"/>. Per-file failures (missing or
    /// corrupted chunks, size or hash mismatches) are collected rather than aborting the run, so the
    /// result reports every affected file.
    /// </summary>
    /// <remarks>
    /// A chunk file whose content fails authentication is renamed with the
    /// <see cref="BackupConstants.QuarantineExtension"/> extension once the run has finished. The
    /// manifest then simply misses that chunk, which the next update regenerates from the source,
    /// while the damaged bytes stay on disk until that update prunes them.
    /// </remarks>
    /// <param name="sourcePath">The directory containing the backup chunks and manifest.</param>
    /// <param name="request">The backup request carrying the password used to decrypt the manifest.</param>
    /// <param name="progress">A sink that receives incremental status updates.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A result whose value reports how many files verified successfully and any integrity errors.</returns>
    internal async Task<Result<BackupResult>> VerifyAsync(
        string sourcePath,
        VerifyBackupRequest request,
        IProgress<BackupStatus> progress,
        CancellationToken cancellationToken
    )
    {
        var stopwatch = Stopwatch.StartNew();

        var (backup, openError) = await backupOpener.OpenAsync(
                sourcePath,
                request.Password,
                MessageCode.ManifestRequiredForVerify,
                MessageCode.VerifyInvalidPassword,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (backup is null)
        {
            return Result<BackupResult>.Failure(openError!);
        }

        using (backup)
        {
            var manifest = backup.Manifest;
            var reader = this.CreateChunkReader(backup, sourcePath);

            ManifestEntries.Validate(manifest.Files);

            var totalFiles = manifest.Files.Count;
            ProgressTracker tracker = new(
                progress,
                stopwatch,
                totalFiles,
                manifest.Files.Sum(static f => f.TotalSize)
            );
            tracker.ReportStart();

            ConcurrentBag<LocalizableMessage> errors = [];
            ConcurrentDictionary<string, byte> damagedChunks = new(StringComparer.Ordinal);
            long verifiedBytes = 0;
            var processedFiles = 0;

            await Parallel
                .ForEachAsync(
                    manifest.Files,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = EngineLimits.MaximumParallelFileOperations,
                        CancellationToken = cancellationToken,
                    },
                    async (fileEntry, token) =>
                    {
                        try
                        {
                            await reader.ReadFileAsync(
                                    fileEntry,
                                    Stream.Null,
                                    tracker,
                                    chunkFile => damagedChunks.TryAdd(chunkFile, 0),
                                    token
                                )
                                .ConfigureAwait(false);

                            _ = Interlocked.Increment(ref processedFiles);
                            _ = Interlocked.Add(ref verifiedBytes, fileEntry.TotalSize);
                            tracker.CompleteFile();
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            errors.Add(
                                new LocalizableMessage(
                                    MessageCode.IntegrityErrorFormat,
                                    fileEntry.OriginalPath,
                                    FailureReason.From(ex)
                                )
                            );
                        }
                    }
                )
                .ConfigureAwait(false);

            var setAside = this.QuarantineChunks(damagedChunks.Keys);
            stopwatch.Stop();

            return Result<BackupResult>.Success(
                new BackupResult(
                    stopwatch.Elapsed,
                    verifiedBytes,
                    processedFiles,
                    totalFiles,
                    errors: OrderMessages(errors),
                    warnings: setAside > 0
                        ? [new LocalizableMessage(MessageCode.DamagedChunksSetAsideFormat, setAside)]
                        : null
                )
            );
        }
    }

    /// <summary>
    /// Decides where each recorded file is restored, reporting the files that cannot be restored on
    /// this system instead of letting one of them fail the whole restore.
    /// </summary>
    /// <param name="manifest">The decrypted manifest.</param>
    /// <param name="backupRoot">The backup folder being read.</param>
    /// <param name="destinationPath">The restore root.</param>
    /// <param name="errors">The collector the per-file problems are added to.</param>
    /// <returns>The files to restore and their target paths, in manifest order.</returns>
    private static List<RestoreItem> PlanRestore(
        ChunkManifestData manifest,
        string backupRoot,
        string destinationPath,
        ConcurrentBag<LocalizableMessage> errors
    )
    {
        var backupPrefix = WithTrailingSeparator(Path.GetFullPath(backupRoot));
        Dictionary<string, string> claimedTargets = new(
            StringComparer.FromComparison(PathNormalizationHelper.PathComparer)
        );
        List<RestoreItem> plan = [];

        foreach (var entry in manifest.Files)
        {
            if (!ManifestPathPolicy.IsValidOnThisSystem(entry.OriginalPath))
            {
                errors.Add(
                    new LocalizableMessage(MessageCode.RestoreNameNotSupportedFormat, entry.OriginalPath)
                );
                continue;
            }

            string targetPath;
            try
            {
                targetPath = RestorePathGuard.ResolveSafeDestination(destinationPath, entry.OriginalPath);
            }
            catch (InvalidDataException exception)
            {
                errors.Add(
                    new LocalizableMessage(
                        MessageCode.DecryptionErrorFormat,
                        entry.OriginalPath,
                        FailureReason.From(exception)
                    )
                );
                continue;
            }

            if (targetPath.StartsWith(backupPrefix, PathNormalizationHelper.PathComparer))
            {
                errors.Add(
                    new LocalizableMessage(MessageCode.RestoreTargetInsideBackupFormat, entry.OriginalPath)
                );
                continue;
            }

            if (claimedTargets.TryGetValue(targetPath, out var owner))
            {
                errors.Add(
                    new LocalizableMessage(MessageCode.RestoreNameConflictFormat, entry.OriginalPath, owner)
                );
                continue;
            }

            claimedTargets[targetPath] = entry.OriginalPath;
            plan.Add(new RestoreItem(entry, targetPath));
        }

        return plan;
    }

    /// <summary>
    /// Recreates the empty folders the manifest records, reporting any that cannot be created.
    /// </summary>
    /// <param name="directories">The recorded folders, or <see langword="null"/> when there are none.</param>
    /// <param name="backupRoot">The backup folder being read.</param>
    /// <param name="destinationPath">The restore root.</param>
    /// <param name="errors">The collector the per-folder problems are added to.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes once every folder has been handled.</returns>
    private async Task RestoreDirectoriesAsync(
        IReadOnlyList<string>? directories,
        string backupRoot,
        string destinationPath,
        ConcurrentBag<LocalizableMessage> errors,
        CancellationToken cancellationToken
    )
    {
        var backupPrefix = WithTrailingSeparator(Path.GetFullPath(backupRoot));

        foreach (var directory in directories ?? [])
        {
            if (!ManifestPathPolicy.IsValidOnThisSystem(directory))
            {
                errors.Add(new LocalizableMessage(MessageCode.RestoreNameNotSupportedFormat, directory));
                continue;
            }

            try
            {
                var targetPath = RestorePathGuard.ResolveSafeDestination(destinationPath, directory);

                if (targetPath.StartsWith(backupPrefix, PathNormalizationHelper.PathComparer))
                {
                    errors.Add(
                        new LocalizableMessage(MessageCode.RestoreTargetInsideBackupFormat, directory)
                    );
                    continue;
                }

                RestorePathGuard.EnsureNoReparsePointDescendants(fileOperationsService, destinationPath, targetPath);
                await fileOperationsService
                    .CreateDirectoryAsync(targetPath, cancellationToken)
                    .ConfigureAwait(false);
                RestorePathGuard.EnsureNoReparsePointDescendants(fileOperationsService, destinationPath, targetPath);
            }
            catch (Exception exception)
                when (exception is not OperationCanceledException && FileFailures.IsConfinedToOneFile(exception))
            {
                errors.Add(
                    new LocalizableMessage(
                        MessageCode.DecryptionErrorFormat,
                        directory,
                        FailureReason.From(exception)
                    )
                );
            }
        }
    }

    /// <summary>
    /// Renames damaged chunk files so the next update regenerates them. A chunk that cannot be
    /// renamed, for example on read-only media, is left as it is.
    /// </summary>
    /// <param name="chunkFiles">The chunk files that failed authentication.</param>
    /// <returns>The number of chunk files set aside.</returns>
    private int QuarantineChunks(IEnumerable<string> chunkFiles)
    {
        var count = 0;

        foreach (var chunkFile in chunkFiles.Order(StringComparer.Ordinal))
        {
            try
            {
                fileOperationsService.MoveFile(chunkFile, chunkFile + BackupConstants.QuarantineExtension);
                count++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }
        }

        return count;
    }

    /// <summary>
    /// Builds the reader that opens the chunks of an opened backup.
    /// </summary>
    /// <param name="backup">The opened backup.</param>
    /// <param name="backupRoot">The backup folder.</param>
    /// <returns>The chunk reader.</returns>
    private ChunkReader CreateChunkReader(OpenedBackup backup, string backupRoot)
    {
        return new ChunkReader(
            fileOperationsService,
            Path.Join(backupRoot, BackupConstants.ChunksDirectoryName),
            ChunkCipherSet.For(
                backup.Keys,
                encryptionStrategies.Resolve(backup.Manifest.Header.EncryptionAlgorithm),
                compressionStrategies.ResolveOrNull(backup.Manifest.Header.Compression)
            )
        );
    }

    /// <summary>
    /// Orders collected messages by the path they name, so a report reads the same on every run.
    /// </summary>
    /// <param name="messages">The collected messages.</param>
    /// <returns>The messages ordered by their first argument.</returns>
    private static List<LocalizableMessage> OrderMessages(IEnumerable<LocalizableMessage> messages)
    {
        return
        [
            .. messages.OrderBy(
                static m => m.Args.Count > 0 ? m.Args[0] as string ?? string.Empty : string.Empty,
                StringComparer.Ordinal
            ),
        ];
    }

    /// <summary>
    /// Appends a directory separator to a full path unless it already ends with one.
    /// </summary>
    /// <param name="path">The full path.</param>
    /// <returns>The path terminated by a directory separator.</returns>
    private static string WithTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
    }

    /// <summary>
    /// Reconstructs one backed-up file on disk, creating its parent directory, writing the decrypted
    /// chunks to a path confined to the restore root, and then applying the recorded modification time
    /// and attributes.
    /// </summary>
    /// <param name="fileEntry">The manifest entry describing the file and its chunks.</param>
    /// <param name="destFilePath">The resolved path the file is written to.</param>
    /// <param name="destinationPath">The restore root the file is written beneath.</param>
    /// <param name="reader">The reader that opens the backup's chunks.</param>
    /// <param name="tracker">The progress tracker that is told about every chunk written.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the file has been reconstructed and checked against the manifest.</returns>
    private async Task RestoreFileFromChunksAsync(
        ChunkManifestFileEntry fileEntry,
        string destFilePath,
        string destinationPath,
        ChunkReader reader,
        ProgressTracker tracker,
        CancellationToken cancellationToken
    )
    {
        var destDir = Path.GetDirectoryName(destFilePath);

        if (!string.IsNullOrEmpty(destDir))
        {
            RestorePathGuard.EnsureNoReparsePointDescendants(fileOperationsService, destinationPath, destDir);
            await fileOperationsService
                .CreateDirectoryAsync(destDir, cancellationToken)
                .ConfigureAwait(false);
            RestorePathGuard.EnsureNoReparsePointDescendants(fileOperationsService, destinationPath, destDir);
        }

        await fileOperationsService
            .WriteFileAtomicallyAsync(
                destFilePath,
                (destStream, token) =>
                    reader.ReadFileAsync(fileEntry, destStream, tracker, null, token),
                cancellationToken
            )
            .ConfigureAwait(false);

        this.TryApplyMetadata(destFilePath, fileEntry);
    }

    /// <summary>
    /// Applies the modification time and attributes recorded for a restored file. The content is
    /// already in place, so a file system that cannot store them leaves the file as written.
    /// </summary>
    /// <param name="filePath">The restored file.</param>
    /// <param name="fileEntry">The manifest entry carrying the recorded metadata.</param>
    private void TryApplyMetadata(string filePath, ChunkManifestFileEntry fileEntry)
    {
        try
        {
            fileMetadataService.ApplyFileMetadata(
                filePath,
                fileEntry.LastWriteTimeUtc,
                fileEntry.Attributes?.HasFlag(ManifestFileAttributes.ReadOnly),
                fileEntry.Attributes?.HasFlag(ManifestFileAttributes.Hidden),
                fileEntry.UnixMode
            );
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or ArgumentException or PlatformNotSupportedException)
        {
            return;
        }
    }

    /// <summary>
    /// A recorded file a restore writes, and where.
    /// </summary>
    /// <param name="Entry">The manifest entry.</param>
    /// <param name="TargetPath">The resolved path the file is written to.</param>
    private sealed record class RestoreItem(ChunkManifestFileEntry Entry, string TargetPath);
}
