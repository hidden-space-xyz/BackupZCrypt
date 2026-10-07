using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Services.Interfaces;

namespace BackupZCrypt.Application.Utilities.Helpers;

/// <summary>
/// Recognizes the files and folders this program writes into a backup folder, so cleanup only ever
/// touches its own files and a create can tell a folder holding a backup from one holding user data.
/// </summary>
internal static class BackupLayout
{
    /// <summary>
    /// The number of hexadecimal characters in a chunk file name: the HMAC-SHA256 of the chunk hash.
    /// </summary>
    private const int ChunkNameHexLength = 64;

    /// <summary>
    /// The number of hexadecimal characters in the random part of a temporary file name.
    /// </summary>
    private const int TemporaryNameHexLength = 24;

    /// <summary>
    /// Determines whether a name is that of a stored chunk file.
    /// </summary>
    /// <param name="fileName">The file name to test.</param>
    /// <returns><see langword="true"/> for a chunk file name.</returns>
    internal static bool IsChunkFileName(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        return fileName.Length == ChunkNameHexLength + BackupConstants.AppFileExtension.Length
            && fileName.EndsWith(BackupConstants.AppFileExtension, StringComparison.OrdinalIgnoreCase)
            && IsHex(fileName.AsSpan(0, ChunkNameHexLength));
    }

    /// <summary>
    /// Determines whether a name is that of a chunk file verification set aside as damaged.
    /// </summary>
    /// <param name="fileName">The file name to test.</param>
    /// <returns><see langword="true"/> for a quarantined chunk file name.</returns>
    internal static bool IsQuarantinedChunkFileName(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        return fileName.EndsWith(BackupConstants.QuarantineExtension, StringComparison.OrdinalIgnoreCase)
            && IsChunkFileName(fileName[..^BackupConstants.QuarantineExtension.Length]);
    }

    /// <summary>
    /// Determines whether a name is that of a temporary file written before an atomic rename.
    /// </summary>
    /// <param name="fileName">The file name to test.</param>
    /// <returns><see langword="true"/> for a temporary file name.</returns>
    internal static bool IsTemporaryFileName(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        return fileName.Length == 1 + TemporaryNameHexLength + BackupConstants.TemporaryFileExtension.Length
            && fileName[0] is '.'
            && fileName.EndsWith(BackupConstants.TemporaryFileExtension, StringComparison.OrdinalIgnoreCase)
            && IsHex(fileName.AsSpan(1, TemporaryNameHexLength));
    }

    /// <summary>
    /// Determines whether a folder holds nothing but a backup written by this program: its manifest,
    /// its chunks directory with chunk files only, its lock file, and leftover temporary files.
    /// </summary>
    /// <param name="fileOperationsService">The service used to list the folder.</param>
    /// <param name="directoryPath">The folder to inspect.</param>
    /// <param name="containsManifest">Receives whether the folder holds a manifest.</param>
    /// <returns><see langword="true"/> when every entry belongs to a backup.</returns>
    internal static bool ContainsOnlyBackupFiles(
        IFileOperationsService fileOperationsService,
        string directoryPath,
        out bool containsManifest
    )
    {
        ArgumentNullException.ThrowIfNull(fileOperationsService);

        containsManifest = false;

        foreach (var name in fileOperationsService.GetDirectoryEntryNames(directoryPath))
        {
            var fullPath = fileOperationsService.CombinePath(directoryPath, name);

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
                    || IsTemporaryFileName(name))
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
                IsChunkFileName(name) || IsQuarantinedChunkFileName(name) || IsTemporaryFileName(name)
            );
    }

    /// <summary>
    /// Determines whether every character is a hexadecimal digit.
    /// </summary>
    /// <param name="value">The characters to test.</param>
    /// <returns><see langword="true"/> when every character is hexadecimal.</returns>
    private static bool IsHex(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
        {
            if (!char.IsAsciiHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }
}
