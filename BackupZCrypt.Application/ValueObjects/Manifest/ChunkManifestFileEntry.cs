namespace BackupZCrypt.Application.ValueObjects.Manifest;

/// <summary>
/// A single backed-up file within a chunked manifest, including the chunks that reconstruct it.
/// </summary>
/// <param name="OriginalPath">The file's path relative to the backup root.</param>
/// <param name="FileHash">The Base64-encoded SHA-256 hash of the whole file, used to verify restores.</param>
/// <param name="TotalSize">The original file size in bytes.</param>
/// <param name="Chunks">The ordered chunk references that reconstruct the file.</param>
/// <param name="LastWriteTimeUtc">
/// The file's modification time in UTC, or <see langword="null"/> for entries written before it was
/// recorded. An update also compares it with the source to skip unchanged files without reading them.
/// </param>
/// <param name="Attributes">The portable attributes recorded for the file, or <see langword="null"/> when none were.</param>
/// <param name="UnixMode">The Unix permission bits, or <see langword="null"/> when the file came from Windows.</param>
public sealed record class ChunkManifestFileEntry(
    string OriginalPath,
    string FileHash,
    long TotalSize,
    IReadOnlyList<ChunkManifestChunkRef> Chunks,
    DateTime? LastWriteTimeUtc = null,
    ManifestFileAttributes? Attributes = null,
    int? UnixMode = null
);
