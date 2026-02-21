namespace BackupZCrypt.Application.ValueObjects.Manifest;

using BackupZCrypt.Domain.Enums;

public sealed record ManifestHeader(
    EncryptionAlgorithm EncryptionAlgorithm,
    KeyDerivationAlgorithm KeyDerivationAlgorithm,
    NameObfuscationMode NameObfuscation,
    CompressionMode Compression);
