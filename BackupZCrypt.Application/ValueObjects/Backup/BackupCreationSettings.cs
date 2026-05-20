using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Domain.Enums;

namespace BackupZCrypt.Application.ValueObjects.Backup;

public sealed record BackupCreationSettings(
    EncryptionAlgorithm EncryptionAlgorithm = EncryptionAlgorithm.Aes,
    KeyDerivationAlgorithm KeyDerivationAlgorithm = KeyDerivationAlgorithm.Argon2id,
    CompressionMode CompressionMode = CompressionMode.None)
    : ISettings<BackupCreationSettings>
{
    public static BackupCreationSettings DefaultValue { get; } = new();

    public static string FileName => "backup-creation-settings.json";
}