namespace BackupZCrypt.Domain.Constants;

/// <summary>
/// Well-known names and extensions used by the backup file format and layout.
/// </summary>
public static class BackupConstants
{
    /// <summary>
    /// The maximum plaintext size of one content-defined chunk (4 MiB). This is part of the
    /// on-disk format: readers use it to reject impossible manifest entries and oversized chunk
    /// files before allocating memory for them.
    /// </summary>
    public const int MaximumChunkSize = 4 * 1024 * 1024;

    /// <summary>
    /// The file extension used for backup artifacts produced by this tool.
    /// </summary>
    public const string AppFileExtension = ".bzc";

    /// <summary>
    /// The file name of the encrypted manifest that stores restore metadata.
    /// </summary>
    public const string ManifestFileName = "manifest" + AppFileExtension;

    /// <summary>
    /// The name of the directory that holds the encrypted chunk files.
    /// </summary>
    public const string ChunksDirectoryName = "chunks";

    /// <summary>
    /// The name of the lock file a create or update holds open in the backup folder, so a second
    /// operation on the same backup is refused instead of interleaving its writes.
    /// </summary>
    public const string LockFileName = "backup.lock";

    /// <summary>
    /// The extension of the randomly named temporary files written next to their final path and
    /// renamed into place once complete.
    /// </summary>
    public const string TemporaryFileExtension = ".tmp";

    /// <summary>
    /// The extension appended to a chunk file that failed authentication during verification, which
    /// sets it aside so the next update regenerates the chunk from the source.
    /// </summary>
    public const string QuarantineExtension = ".corrupt";
}
