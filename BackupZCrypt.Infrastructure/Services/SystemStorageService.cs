using BackupZCrypt.Domain.Services.Interfaces;

namespace BackupZCrypt.Infrastructure.Services;

/// <summary>
/// Queries the local file system for drive/volume information used to validate backup
/// destinations. Every method swallows I/O exceptions and returns a sentinel value so
/// callers can treat an unavailable or invalid path as a non-fatal condition.
/// </summary>
internal sealed class SystemStorageService : ISystemStorageService
{
    /// <summary>
    /// Returns the volume root (e.g. <c>C:\</c>) of the supplied path.
    /// </summary>
    /// <param name="fullPath">The path whose root is requested.</param>
    /// <returns>The path root, or <see langword="null"/> if it cannot be determined.</returns>
    public string? GetPathRoot(string fullPath)
    {
        try
        {
            return Path.GetPathRoot(fullPath);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the number of bytes available on the drive identified by <paramref name="rootPath"/>.
    /// </summary>
    /// <param name="rootPath">The drive root to inspect.</param>
    /// <returns>The available free space in bytes, or <c>-1</c> if the drive is not ready or cannot be read.</returns>
    public long GetAvailableFreeSpace(string rootPath)
    {
        try
        {
            DriveInfo driveInfo = new(rootPath);
            return driveInfo.IsReady ? driveInfo.AvailableFreeSpace : -1;
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>
    /// Determines whether the drive identified by <paramref name="rootPath"/> is mounted and ready for access.
    /// </summary>
    /// <param name="rootPath">The drive root to check.</param>
    /// <returns><see langword="true"/> if the drive is ready; otherwise <see langword="false"/>.</returns>
    public bool IsDriveReady(string rootPath)
    {
        try
        {
            DriveInfo driveInfo = new(rootPath);
            return driveInfo.IsReady;
        }
        catch
        {
            return false;
        }
    }
}
