namespace BackupZCrypt.Domain.Services.Interfaces;

/// <summary>
/// Provides access to host storage information such as drive roots, free space and readiness.
/// </summary>
public interface ISystemStorageService
{
    /// <summary>
    /// Returns the root portion (drive or volume) of the given full path.
    /// </summary>
    /// <param name="fullPath">The path whose root is requested.</param>
    /// <returns>The root of <paramref name="fullPath"/>, or <see langword="null"/> if it has none.</returns>
    string? GetPathRoot(string fullPath);

    /// <summary>
    /// Gets the number of bytes available on the drive identified by the given root.
    /// </summary>
    /// <param name="rootPath">The root (drive or volume) to query.</param>
    /// <returns>The available free space in bytes.</returns>
    long GetAvailableFreeSpace(string rootPath);

    /// <summary>
    /// Determines whether the drive identified by the given root is present and ready for I/O.
    /// </summary>
    /// <param name="rootPath">The root (drive or volume) to check.</param>
    /// <returns><see langword="true"/> if the drive is ready; otherwise <see langword="false"/>.</returns>
    bool IsDriveReady(string rootPath);
}
