namespace BackupZCrypt.Domain.ValueObjects.FileSystem;

/// <summary>
/// The attributes of a file that a backup records so a restore can reproduce them.
/// </summary>
/// <param name="Size">The file length in bytes.</param>
/// <param name="LastWriteTimeUtc">The time the file was last modified, in UTC.</param>
/// <param name="IsReadOnly">Whether the file is marked read-only.</param>
/// <param name="IsHidden">Whether the file is marked hidden; only meaningful on Windows.</param>
/// <param name="UnixMode">The Unix permission bits, or <see langword="null"/> on Windows.</param>
public sealed record class FileMetadata(
    long Size,
    DateTime LastWriteTimeUtc,
    bool IsReadOnly,
    bool IsHidden,
    int? UnixMode
);
