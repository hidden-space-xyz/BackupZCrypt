using BackupZCrypt.Application.FileSystem;

namespace BackupZCrypt.Infrastructure.Services;

/// <summary>
/// File-system implementation of <see cref="IFileMetadataService"/> backed by <see cref="System.IO"/>.
/// </summary>
internal sealed class FileMetadataService : IFileMetadataService
{
    /// <summary>
    /// Reads the length, modification time, and attributes of a file. The hidden flag is recorded only
    /// on Windows, where it is a real attribute rather than a reflection of a leading dot, and the
    /// permission bits only on Unix.
    /// </summary>
    /// <param name="filePath">The path of the file to inspect.</param>
    /// <returns>The file's metadata.</returns>
    public FileMetadata GetFileMetadata(string filePath)
    {
        var info = new FileInfo(filePath);
        var attributes = info.Attributes;

        return new FileMetadata(
            info.Length,
            info.LastWriteTimeUtc,
            attributes.HasFlag(FileAttributes.ReadOnly),
            OperatingSystem.IsWindows() && attributes.HasFlag(FileAttributes.Hidden),
            OperatingSystem.IsWindows() ? null : (int)File.GetUnixFileMode(filePath)
        );
    }

    /// <summary>
    /// Applies the recorded modification time first, while the file is still writable, and the
    /// attributes afterwards. On Unix the permission bits take precedence over the read-only flag,
    /// which they already express.
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
    )
    {
        if (lastWriteTimeUtc is { } lastWrite)
        {
            File.SetLastWriteTimeUtc(filePath, DateTime.SpecifyKind(lastWrite, DateTimeKind.Utc));
        }

        if (!OperatingSystem.IsWindows())
        {
            if (unixMode is { } mode)
            {
                File.SetUnixFileMode(filePath, (UnixFileMode)mode & (UnixFileMode)0x1FF);
            }
            else if (isReadOnly is true)
            {
                File.SetUnixFileMode(
                    filePath,
                    File.GetUnixFileMode(filePath)
                        & ~(UnixFileMode.UserWrite | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)
                );
            }

            return;
        }

        var attributes = File.GetAttributes(filePath);
        var updated = attributes;

        if (isHidden is { } hidden)
        {
            updated = hidden ? updated | FileAttributes.Hidden : updated & ~FileAttributes.Hidden;
        }

        if (isReadOnly is { } readOnly)
        {
            updated = readOnly ? updated | FileAttributes.ReadOnly : updated & ~FileAttributes.ReadOnly;
        }

        if (updated != attributes)
        {
            File.SetAttributes(filePath, updated);
        }
    }
}
