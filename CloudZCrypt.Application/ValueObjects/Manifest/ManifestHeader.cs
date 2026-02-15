using CloudZCrypt.Domain.Enums;

namespace CloudZCrypt.Application.ValueObjects.Manifest;

public sealed record ManifestHeader(
    EncryptionAlgorithm EncryptionAlgorithm,
    KeyDerivationAlgorithm KeyDerivationAlgorithm,
    NameObfuscationMode NameObfuscation,
    CompressionMode Compression
);
