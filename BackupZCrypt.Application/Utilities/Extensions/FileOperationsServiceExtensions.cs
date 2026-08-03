using BackupZCrypt.Domain.Services.Interfaces;

namespace BackupZCrypt.Application.Utilities.Extensions;

/// <summary>
/// File-system probes whose failure must not change the outcome of the operation that triggered them.
/// </summary>
/// <remarks>
/// Removing a temporary file, pruning an orphaned chunk, and measuring a file to seed a progress bar
/// are all housekeeping: the archive is already consistent without them, so a locked, vanished, or
/// unreadable file has to leave a harmless leftover rather than fail a backup the user cannot cheaply
/// retry. Centralizing the swallow here keeps that decision in one reviewable place instead of
/// scattering bare <see langword="catch"/> blocks through the engine, and keeps
/// <see cref="OutOfMemoryException"/> propagating, which no caller can meaningfully continue past.
/// </remarks>
internal static class FileOperationsServiceExtensions
{
    /// <summary>
    /// Deletes a file, reporting failure instead of throwing.
    /// </summary>
    /// <param name="fileOperationsService">The service that performs the deletion.</param>
    /// <param name="filePath">The full path of the file to delete.</param>
    /// <returns><see langword="true"/> if the file was deleted; otherwise <see langword="false"/>.</returns>
    public static bool TryDeleteFile(
        this IFileOperationsService fileOperationsService,
        string filePath
    )
    {
        ArgumentNullException.ThrowIfNull(fileOperationsService);

        try
        {
            fileOperationsService.DeleteFile(filePath);
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads a file's size, reporting failure instead of throwing.
    /// </summary>
    /// <param name="fileOperationsService">The service that performs the measurement.</param>
    /// <param name="filePath">The full path of the file to measure.</param>
    /// <param name="size">The file size in bytes, or zero when it could not be read.</param>
    /// <returns><see langword="true"/> if the size was read; otherwise <see langword="false"/>.</returns>
    public static bool TryGetFileSize(
        this IFileOperationsService fileOperationsService,
        string filePath,
        out long size
    )
    {
        ArgumentNullException.ThrowIfNull(fileOperationsService);

        try
        {
            size = fileOperationsService.GetFileSize(filePath);
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            size = 0;
            return false;
        }
    }
}
