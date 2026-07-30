using BackupZCrypt.Domain.Enums;

namespace BackupZCrypt.Domain.ValueObjects.Backup;

/// <summary>
/// Describes a backup, restore, or update operation and the cryptographic options it should use.
/// </summary>
/// <param name="SourcePath">The file or directory to back up, or the backup to restore from.</param>
/// <param name="DestinationPath">The location where the backup is written or restored to.</param>
/// <param name="Password">The password used to derive keys and protect the data.</param>
/// <param name="ConfirmPassword">The password confirmation, validated against <paramref name="Password"/>.</param>
/// <param name="EncryptionAlgorithm">The AEAD cipher to encrypt chunks and the manifest with.</param>
/// <param name="KeyDerivationAlgorithm">The key derivation function used to derive the master key.</param>
/// <param name="Operation">The kind of operation to perform.</param>
/// <param name="Compression">The compression mode applied to chunks before encryption.</param>
/// <param name="ProceedOnWarnings">Whether to continue when validation produces non-fatal warnings.</param>
public sealed record BackupRequest(
    string SourcePath,
    string DestinationPath,
    string Password,
    string ConfirmPassword,
    EncryptionAlgorithm EncryptionAlgorithm,
    KeyDerivationAlgorithm KeyDerivationAlgorithm,
    BackupOperation Operation,
    CompressionMode Compression = CompressionMode.None,
    bool ProceedOnWarnings = false
);
