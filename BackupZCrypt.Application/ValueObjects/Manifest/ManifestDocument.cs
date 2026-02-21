namespace BackupZCrypt.Application.ValueObjects.Manifest;

using BackupZCrypt.Domain.Enums;

internal sealed record ManifestDocument(
    EncryptionAlgorithm EncryptionAlgorithm,
    KeyDerivationAlgorithm KeyDerivationAlgorithm,
    NameObfuscationMode NameObfuscation,
    CompressionMode Compression,
    List<ManifestEntry> Entries);
