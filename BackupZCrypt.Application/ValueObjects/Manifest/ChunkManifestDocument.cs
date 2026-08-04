using BackupZCrypt.Domain.Enums;

namespace BackupZCrypt.Application.ValueObjects.Manifest;

/// <summary>
/// The JSON-serializable on-disk shape of a chunked backup manifest before encryption.
/// </summary>
/// <param name="EncryptionAlgorithm">The encryption algorithm used for the backup.</param>
/// <param name="KeyDerivationAlgorithm">The key derivation algorithm used for the backup.</param>
/// <param name="Compression">The compression mode applied to chunks.</param>
/// <param name="MasterSalt">
/// The Base64-encoded master salt. The same salt is echoed in the unencrypted preamble header, and the two are
/// compared in constant time after decryption to detect tampering.
/// </param>
/// <param name="Files">The serialized file entries contained in the backup.</param>
internal sealed record class ChunkManifestDocument(
    EncryptionAlgorithm EncryptionAlgorithm,
    KeyDerivationAlgorithm KeyDerivationAlgorithm,
    CompressionMode Compression,
    string MasterSalt,
    List<ChunkManifestFileEntrySerialized> Files
);
