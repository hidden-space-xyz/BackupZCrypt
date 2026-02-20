namespace CloudZCrypt.Application.ValueObjects.Manifest;

using CloudZCrypt.Domain.Enums;

public sealed record ManifestHeader(
    EncryptionAlgorithm EncryptionAlgorithm,
    KeyDerivationAlgorithm KeyDerivationAlgorithm,
    NameObfuscationMode NameObfuscation,
    CompressionMode Compression);
