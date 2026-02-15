using CloudZCrypt.Domain.Enums;

namespace CloudZCrypt.Application.ValueObjects.Manifest;

internal sealed record ManifestDocument(
    EncryptionAlgorithm EncryptionAlgorithm,
    KeyDerivationAlgorithm KeyDerivationAlgorithm,
    NameObfuscationMode NameObfuscation,
    CompressionMode Compression,
    List<ManifestEntry> Entries
);
