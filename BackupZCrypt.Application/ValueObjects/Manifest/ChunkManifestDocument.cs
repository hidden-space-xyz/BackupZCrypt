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
internal sealed record ChunkManifestDocument(
    EncryptionAlgorithm EncryptionAlgorithm,
    KeyDerivationAlgorithm KeyDerivationAlgorithm,
    CompressionMode Compression,
    string MasterSalt,
    List<ChunkManifestFileEntrySerialized> Files
);

/// <summary>
/// The JSON-serializable on-disk shape of a single backed-up file entry.
/// </summary>
/// <param name="OriginalPath">The file's path relative to the backup root.</param>
/// <param name="FileHash">The Base64-encoded SHA-256 hash of the whole file, used to verify restores.</param>
/// <param name="TotalSize">The original file size in bytes.</param>
/// <param name="Chunks">The ordered chunk references that reconstruct the file.</param>
internal sealed record ChunkManifestFileEntrySerialized(
    string OriginalPath,
    string FileHash,
    long TotalSize,
    List<ChunkManifestChunkRef> Chunks
);
