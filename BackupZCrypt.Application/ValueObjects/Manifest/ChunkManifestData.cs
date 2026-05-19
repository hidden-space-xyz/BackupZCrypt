namespace BackupZCrypt.Application.ValueObjects.Manifest;

public sealed record ChunkManifestData(
    ManifestHeader Header,
    string MasterSalt,
    IReadOnlyList<ChunkManifestFileEntry> Files);
