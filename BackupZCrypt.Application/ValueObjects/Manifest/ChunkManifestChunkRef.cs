namespace BackupZCrypt.Application.ValueObjects.Manifest;

public sealed record ChunkManifestChunkRef(
    string Hash,
    int Size,
    string Nonce);
