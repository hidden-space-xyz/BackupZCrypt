namespace BackupZCrypt.Application.FileSystem;

/// <summary>
/// Reads the metadata a backup records for a file and applies it again when the file is restored.
/// </summary>
public interface IFileMetadataService
{
    /// <summary>
    /// Reads the size, modification time, and attributes a backup records for a file.
    /// </summary>
    /// <param name="filePath">The path of the file to inspect.</param>
    /// <returns>The file's metadata.</returns>
    public FileMetadata GetFileMetadata(string filePath);

    /// <summary>
    /// Restores the modification time and attributes recorded for a file. Values that were not
    /// recorded are left as the file system set them.
    /// </summary>
    /// <param name="filePath">The path of the restored file.</param>
    /// <param name="lastWriteTimeUtc">The modification time to apply, or <see langword="null"/>.</param>
    /// <param name="isReadOnly">Whether to mark the file read-only, or <see langword="null"/>.</param>
    /// <param name="isHidden">Whether to mark the file hidden, or <see langword="null"/>.</param>
    /// <param name="unixMode">The Unix permission bits to apply, or <see langword="null"/>.</param>
    public void ApplyFileMetadata(
        string filePath,
        DateTime? lastWriteTimeUtc,
        bool? isReadOnly,
        bool? isHidden,
        int? unixMode
    );
}
