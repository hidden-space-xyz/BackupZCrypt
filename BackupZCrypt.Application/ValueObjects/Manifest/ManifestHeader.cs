namespace BackupZCrypt.Application.ValueObjects.Manifest;

using BackupZCrypt.Domain.Enums;

public sealed record ManifestHeader(
    EncryptionAlgorithm EncryptionAlgorithm,
    KeyDerivationAlgorithm KeyDerivationAlgorithm,
    CompressionMode Compression);