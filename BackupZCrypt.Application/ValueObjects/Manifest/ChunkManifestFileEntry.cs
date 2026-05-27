namespace BackupZCrypt.Application.ValueObjects.Manifest;

public sealed record ChunkManifestFileEntry(
    string OriginalPath,
    string FileHash,
    long TotalSize,
    IReadOnlyList<ChunkManifestChunkRef> Chunks
);
