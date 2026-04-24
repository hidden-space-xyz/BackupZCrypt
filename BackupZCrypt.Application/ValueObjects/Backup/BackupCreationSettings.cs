namespace BackupZCrypt.Application.ValueObjects.Backup;

using BackupZCrypt.Domain.Enums;

public sealed record BackupCreationSettings(
    bool UseEncryption = true,
    EncryptionAlgorithm EncryptionAlgorithm = EncryptionAlgorithm.Aes,
    KeyDerivationAlgorithm KeyDerivationAlgorithm = KeyDerivationAlgorithm.Argon2id,
    NameObfuscationMode NameObfuscationMode = NameObfuscationMode.None,
    CompressionMode CompressionMode = CompressionMode.None)
{
    public static BackupCreationSettings Default { get; } = new();
}
