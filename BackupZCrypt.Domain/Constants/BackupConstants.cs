namespace BackupZCrypt.Domain.Constants;

/// <summary>
/// Well-known names and extensions used by the backup file format and layout.
/// </summary>
public static class BackupConstants
{
    /// <summary>
    /// File extension used for backup artifacts produced by this tool.
    /// </summary>
    public const string AppFileExtension = ".bzc";

    /// <summary>
    /// File name of the encrypted manifest that stores restore metadata.
    /// </summary>
    public const string ManifestFileName = "manifest" + AppFileExtension;

    /// <summary>
    /// Name of the directory that holds the encrypted chunk files.
    /// </summary>
    public const string ChunksDirectoryName = "chunks";
}
