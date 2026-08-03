namespace BackupZCrypt.Application.ValueObjects.Manifest;

/// <summary>
/// The JSON-serializable on-disk shape of a single backed-up file entry.
/// </summary>
/// <param name="OriginalPath">The file's path relative to the backup root.</param>
/// <param name="FileHash">The Base64-encoded SHA-256 hash of the whole file, used to verify restores.</param>
/// <param name="TotalSize">The original file size in bytes.</param>
/// <param name="Chunks">The ordered chunk references that reconstruct the file.</param>
internal sealed record ChunkManifestFileEntrySerialized(
    string OriginalPath,
    string FileHash,
    long TotalSize,
    List<ChunkManifestChunkRef> Chunks
);
