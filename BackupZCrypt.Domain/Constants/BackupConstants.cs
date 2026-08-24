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
    /// The largest encrypted manifest accepted in memory (256 MiB). This operational resource limit
    /// supports very large archives while rejecting files designed to exhaust process memory.
    /// </summary>
    public const int MaximumManifestSize = 256 * 1024 * 1024;

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
}
