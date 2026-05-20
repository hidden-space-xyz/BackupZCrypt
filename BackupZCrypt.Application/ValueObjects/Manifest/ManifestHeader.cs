using BackupZCrypt.Domain.Enums;

namespace BackupZCrypt.Application.ValueObjects.Manifest;

public sealed record ManifestHeader(
    EncryptionAlgorithm EncryptionAlgorithm,
    KeyDerivationAlgorithm KeyDerivationAlgorithm,
    CompressionMode Compression);
