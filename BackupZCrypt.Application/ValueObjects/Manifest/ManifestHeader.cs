using BackupZCrypt.Domain.Enums;

namespace BackupZCrypt.Application.ValueObjects.Manifest;

/// <summary>
/// The algorithm and compression metadata shared by every entry in a backup manifest.
/// </summary>
/// <param name="EncryptionAlgorithm">The encryption algorithm used for the backup.</param>
/// <param name="KeyDerivationAlgorithm">The key derivation algorithm used for the backup.</param>
/// <param name="Compression">The compression mode applied to the backup content.</param>
public sealed record ManifestHeader(
    EncryptionAlgorithm EncryptionAlgorithm,
    KeyDerivationAlgorithm KeyDerivationAlgorithm,
    CompressionMode Compression
);
