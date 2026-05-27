using BackupZCrypt.Domain.Enums;

namespace BackupZCrypt.Application.ValueObjects.Manifest;

internal sealed record ManifestDocument(
    EncryptionAlgorithm EncryptionAlgorithm,
    KeyDerivationAlgorithm KeyDerivationAlgorithm,
    CompressionMode Compression,
    List<ManifestEntry> Entries
);
