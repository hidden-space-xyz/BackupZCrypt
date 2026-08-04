namespace BackupZCrypt.Application.ValueObjects.Manifest;

/// <summary>
/// A single backed-up file within a chunked manifest, including the chunks that reconstruct it.
/// </summary>
/// <param name="OriginalPath">The file's path relative to the backup root.</param>
/// <param name="FileHash">The Base64-encoded SHA-256 hash of the whole file, used to verify restores.</param>
/// <param name="TotalSize">The original file size in bytes.</param>
/// <param name="Chunks">The ordered chunk references that reconstruct the file.</param>
public sealed record class ChunkManifestFileEntry(
    string OriginalPath,
    string FileHash,
    long TotalSize,
    IReadOnlyList<ChunkManifestChunkRef> Chunks
);
