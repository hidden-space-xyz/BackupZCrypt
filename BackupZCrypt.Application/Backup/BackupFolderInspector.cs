using BackupZCrypt.Application.FileSystem;
using BackupZCrypt.Domain.Archive;

namespace BackupZCrypt.Application.Backup;

/// <summary>
/// Inspects a folder through the file-system port to tell one holding a backup written by this
/// program from one holding user data, using the names <see cref="BackupLayout"/> recognizes.
/// </summary>
internal static class BackupFolderInspector
{
    /// <summary>
    /// Determines whether a folder holds nothing but a backup written by this program: its manifest,
    /// its chunks directory with chunk files only, its lock file, and leftover temporary files.
    /// </summary>
    /// <param name="fileOperationsService">The service used to list the folder.</param>
    /// <param name="directoryPath">The folder to inspect.</param>
    /// <param name="containsManifest">Receives whether the folder holds a manifest.</param>
    /// <returns><see langword="true"/> when every entry belongs to a backup.</returns>
    internal static bool ContainsOnlyBackupFiles(
        this IFileOperationsService fileOperationsService,
        string directoryPath,
        out bool containsManifest
    )
    {
        ArgumentNullException.ThrowIfNull(fileOperationsService);

        containsManifest = false;

        foreach (var name in fileOperationsService.GetDirectoryEntryNames(directoryPath))
        {
            var fullPath = Path.Join(directoryPath, name);

            if (string.Equals(name, BackupConstants.ManifestFileName, StringComparison.OrdinalIgnoreCase)
                && fileOperationsService.FileExists(fullPath))
            {
                containsManifest = true;
                continue;
            }

            if (string.Equals(name, BackupConstants.ChunksDirectoryName, StringComparison.OrdinalIgnoreCase)
                && fileOperationsService.DirectoryExists(fullPath)
                && !fileOperationsService.IsReparsePoint(fullPath)
                && ContainsOnlyChunkFiles(fileOperationsService, fullPath))
            {
                continue;
            }

            if ((string.Equals(name, BackupConstants.LockFileName, StringComparison.OrdinalIgnoreCase)
                    || BackupLayout.IsTemporaryFileName(name))
                && fileOperationsService.FileExists(fullPath))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    /// <summary>
    /// Determines whether a chunks directory holds only chunk, quarantined chunk, and temporary files.
    /// </summary>
    /// <param name="fileOperationsService">The service used to list the folder.</param>
    /// <param name="chunksDirectory">The chunks directory to inspect.</param>
    /// <returns><see langword="true"/> when every entry is a file this program writes there.</returns>
    private static bool ContainsOnlyChunkFiles(
        IFileOperationsService fileOperationsService,
        string chunksDirectory
    )
    {
        return fileOperationsService
            .GetDirectoryEntryNames(chunksDirectory)
            .All(static name =>
                BackupLayout.IsChunkFileName(name)
                || BackupLayout.IsQuarantinedChunkFileName(name)
                || BackupLayout.IsTemporaryFileName(name)
            );
    }
}
