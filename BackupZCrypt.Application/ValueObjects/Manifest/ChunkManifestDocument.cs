namespace BackupZCrypt.Application.ValueObjects.Manifest;

using BackupZCrypt.Domain.Enums;

internal sealed record ChunkManifestDocument(
    EncryptionAlgorithm EncryptionAlgorithm,
    KeyDerivationAlgorithm KeyDerivationAlgorithm,
    CompressionMode Compression,
    string MasterSalt,
    List<ChunkManifestFileEntrySerialized> Files);

internal sealed record ChunkManifestFileEntrySerialized(
    string OriginalPath,
    string FileHash,
    long TotalSize,
    List<ChunkManifestChunkRef> Chunks);