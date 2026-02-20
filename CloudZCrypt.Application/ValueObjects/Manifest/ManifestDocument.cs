namespace CloudZCrypt.Application.ValueObjects.Manifest;

using CloudZCrypt.Domain.Enums;

internal sealed record ManifestDocument(
    EncryptionAlgorithm EncryptionAlgorithm,
    KeyDerivationAlgorithm KeyDerivationAlgorithm,
    NameObfuscationMode NameObfuscation,
    CompressionMode Compression,
    List<ManifestEntry> Entries);
