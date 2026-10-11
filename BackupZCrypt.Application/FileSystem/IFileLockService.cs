namespace BackupZCrypt.Application.FileSystem;

/// <summary>
/// Holds and probes the lock file that keeps two processes from modifying one backup at once.
/// </summary>
public interface IFileLockService
{
    /// <summary>
    /// Creates or opens a lock file and holds it exclusively until the returned handle is disposed,
    /// deleting it afterwards.
    /// </summary>
    /// <param name="lockFilePath">The path of the lock file.</param>
    /// <returns>A handle that releases the lock when disposed.</returns>
    /// <exception cref="IOException">Another process holds the lock.</exception>
    public IDisposable AcquireExclusiveLock(string lockFilePath);

    /// <summary>
    /// Determines whether another process currently holds the lock file.
    /// </summary>
    /// <param name="lockFilePath">The path of the lock file.</param>
    /// <returns><see langword="true"/> when the lock file exists and is held open exclusively.</returns>
    public bool IsLockHeld(string lockFilePath);
}
