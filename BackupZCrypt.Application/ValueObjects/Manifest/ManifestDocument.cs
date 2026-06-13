using BackupZCrypt.Domain.Enums;

namespace BackupZCrypt.Application.ValueObjects.Manifest;

/// <summary>
/// The JSON-serializable shape of a plain (non-chunked) backup manifest.
/// </summary>
/// <param name="EncryptionAlgorithm">The encryption algorithm used for the backup.</param>
/// <param name="KeyDerivationAlgorithm">The key derivation algorithm used for the backup.</param>
/// <param name="Compression">The compression mode applied to the backed-up files.</param>
/// <param name="Entries">The per-file manifest entries.</param>
internal sealed record ManifestDocument(
    EncryptionAlgorithm EncryptionAlgorithm,
    KeyDerivationAlgorithm KeyDerivationAlgorithm,
    CompressionMode Compression,
    List<ManifestEntry> Entries
);
