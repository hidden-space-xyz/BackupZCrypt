namespace BackupZCrypt.Application.ValueObjects.Backup;

using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Domain.Enums;

public sealed record BackupCreationSettings(
    bool UseEncryption = true,
    EncryptionAlgorithm EncryptionAlgorithm = EncryptionAlgorithm.Aes,
    KeyDerivationAlgorithm KeyDerivationAlgorithm = KeyDerivationAlgorithm.Argon2id,
    NameObfuscationMode NameObfuscationMode = NameObfuscationMode.None,
    CompressionMode CompressionMode = CompressionMode.None)
    : ISettings<BackupCreationSettings>
{
    public static BackupCreationSettings DefaultValue { get; } = new();

    public static string FileName => "backup-creation-settings.json";
}
