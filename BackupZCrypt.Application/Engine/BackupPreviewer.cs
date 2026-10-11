using BackupZCrypt.Application.Common;
using BackupZCrypt.Application.FileSystem;
using BackupZCrypt.Domain.Archive;

namespace BackupZCrypt.Application.Engine;

/// <summary>
/// Compares what an update or restore is about to do with the backup it works on, without writing
/// anything, so its consequences can be confirmed first.
/// </summary>
/// <param name="backupOpener">The opener of the backup being previewed.</param>
/// <param name="directoryScanService">The service used to walk the source tree an update reads.</param>
/// <param name="fileMetadataService">The service used to read source file metadata.</param>
internal sealed class BackupPreviewer(
    BackupOpener backupOpener,
    IDirectoryScanService directoryScanService,
    IFileMetadataService fileMetadataService
)
{
    /// <summary>
    /// Opens an existing backup and compares it with the source an update would read, without
    /// writing anything.
    /// </summary>
    /// <param name="sourcePath">The source directory the update would read.</param>
    /// <param name="backupPath">The directory containing the existing backup.</param>
    /// <param name="password">The password the backup was created with.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The comparison, or a failure when the backup cannot be opened.</returns>
    internal async Task<Result<BackupPreview>> PreviewUpdateAsync(
        string sourcePath,
        string backupPath,
        string password,
        CancellationToken cancellationToken
    )
    {
        var (backup, openError) = await backupOpener.OpenAsync(
                backupPath,
                password,
                MessageCode.ManifestRequiredForUpdate,
                MessageCode.InvalidPassword,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (backup is null)
        {
            return Result<BackupPreview>.Failure(openError!);
        }

        using (backup)
        {
            var snapshot = await SourceScanner
                .ScanAsync(directoryScanService, sourcePath, cancellationToken)
                .ConfigureAwait(false);

            if (snapshot is null)
            {
                return Result<BackupPreview>.Failure(MessageCode.SourcePathNotExist);
            }

            Dictionary<string, SourceFile> sourceFiles = new(StringComparer.Ordinal);
            foreach (var file in snapshot.Files)
            {
                sourceFiles[file.RelativePath] = file;
            }

            List<string> removedPaths = [];
            var matchedFiles = 0;
            Dictionary<string, ChunkManifestFileEntry> recordedFiles = new(
                StringComparer.Ordinal
            );

            foreach (var entry in backup.Manifest.Files)
            {
                recordedFiles[entry.OriginalPath] = entry;

                if (sourceFiles.ContainsKey(entry.OriginalPath))
                {
                    matchedFiles++;
                }
                else if (!snapshot.IsInsideInaccessibleDirectory(entry.OriginalPath))
                {
                    removedPaths.Add(entry.OriginalPath);
                }
            }

            long bytesToProcess = 0;

            foreach (var file in snapshot.Files)
            {
                var metadata = this.TryGetMetadata(file.FullPath);
                if (metadata is null)
                {
                    continue;
                }

                if (
                    !recordedFiles.TryGetValue(file.RelativePath, out var recorded)
                    || !ManifestEntries.IsUnchanged(recorded, metadata)
                )
                {
                    bytesToProcess = metadata.Size > long.MaxValue - bytesToProcess
                        ? long.MaxValue
                        : bytesToProcess + metadata.Size;
                }
            }

            removedPaths.Sort(StringComparer.Ordinal);

            return Result<BackupPreview>.Success(
                new BackupPreview(
                    backup.Manifest.Files.Count,
                    SumRecordedBytes(backup.Manifest.Files),
                    matchedFiles,
                    removedPaths,
                    bytesToProcess
                )
            );
        }
    }

    /// <summary>
    /// Opens an existing backup and reports what a restore would write, without writing anything.
    /// </summary>
    /// <param name="backupPath">The directory containing the existing backup.</param>
    /// <param name="password">The password the backup was created with.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The backup's contents summary, or a failure when it cannot be opened.</returns>
    internal async Task<Result<BackupPreview>> PreviewRestoreAsync(
        string backupPath,
        string password,
        CancellationToken cancellationToken
    )
    {
        var (backup, openError) = await backupOpener.OpenAsync(
                backupPath,
                password,
                MessageCode.ManifestRequiredForDecryption,
                MessageCode.InvalidPassword,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (backup is null)
        {
            return Result<BackupPreview>.Failure(openError!);
        }

        using (backup)
        {
            var files = backup.Manifest.Files;

            return Result<BackupPreview>.Success(
                new BackupPreview(files.Count, SumRecordedBytes(files), files.Count, [], 0)
            );
        }
    }

    /// <summary>
    /// Adds up the recorded sizes of a manifest's files, saturating instead of overflowing.
    /// </summary>
    /// <param name="files">The manifest entries.</param>
    /// <returns>The total recorded size in bytes.</returns>
    private static long SumRecordedBytes(IEnumerable<ChunkManifestFileEntry> files)
    {
        long total = 0;

        foreach (var file in files)
        {
            total = file.TotalSize > long.MaxValue - total ? long.MaxValue : total + file.TotalSize;
        }

        return total;
    }

    /// <summary>
    /// Reads a file's metadata, reporting a file that vanished or cannot be inspected as absent.
    /// </summary>
    /// <param name="filePath">The file to inspect.</param>
    /// <returns>The metadata, or <see langword="null"/> when it cannot be read.</returns>
    private FileMetadata? TryGetMetadata(string filePath)
    {
        try
        {
            return fileMetadataService.GetFileMetadata(filePath);
        }
        catch (Exception exception) when (FileFailures.IsConfinedToOneFile(exception))
        {
            return null;
        }
    }
}
