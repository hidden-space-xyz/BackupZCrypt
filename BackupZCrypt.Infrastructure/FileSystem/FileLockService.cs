using BackupZCrypt.Application.FileSystem;

namespace BackupZCrypt.Infrastructure.FileSystem;

/// <summary>
/// File-system implementation of <see cref="IFileLockService"/>, holding the lock as an open file
/// that no other process may share.
/// </summary>
internal sealed class FileLockService : IFileLockService
{
    /// <summary>
    /// Opens the lock file with no sharing at all and marks it for deletion once the handle closes,
    /// which the operating system also does when the process dies, so a crash never leaves a lock
    /// that blocks the backup forever.
    /// </summary>
    /// <param name="lockFilePath">The path of the lock file.</param>
    /// <returns>The open lock file, which releases the lock when disposed.</returns>
    public IDisposable AcquireExclusiveLock(string lockFilePath)
    {
        return new FileStream(
            lockFilePath,
            new FileStreamOptions
            {
                Mode = FileMode.OpenOrCreate,
                Access = FileAccess.ReadWrite,
                Share = FileShare.None,
                Options = FileOptions.DeleteOnClose,
            }
        );
    }

    /// <summary>
    /// Probes the lock file with the most permissive sharing: the open only fails while another
    /// process holds it exclusively. A missing file, a file left behind by a crash, or one that cannot
    /// be opened for any other reason is not treated as a held lock.
    /// </summary>
    /// <param name="lockFilePath">The path of the lock file.</param>
    /// <returns><see langword="true"/> when another process holds the lock.</returns>
    public bool IsLockHeld(string lockFilePath)
    {
        if (!File.Exists(lockFilePath))
        {
            return false;
        }

        try
        {
            using FileStream probe = new(
                lockFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete
            );

            return false;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
