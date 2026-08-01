using BackupZCrypt.Domain.Services.Interfaces;
using BackupZCrypt.Domain.Enums;

namespace BackupZCrypt.Application.ValueObjects.Settings;

/// <summary>
/// Persisted defaults preselected in the UI when creating a new backup.
/// </summary>
/// <param name="EncryptionAlgorithm">The encryption algorithm preselected for new backups.</param>
/// <param name="KeyDerivationAlgorithm">The key derivation algorithm preselected for new backups.</param>
/// <param name="CompressionMode">The compression mode preselected for new backups.</param>
public sealed record BackupCreationSettings(
    EncryptionAlgorithm EncryptionAlgorithm = EncryptionAlgorithm.Aes,
    KeyDerivationAlgorithm KeyDerivationAlgorithm = KeyDerivationAlgorithm.Argon2id,
    CompressionMode CompressionMode = CompressionMode.None
) : ISettings<BackupCreationSettings>
{
    /// <summary>
    /// Gets the default settings used when none have been persisted.
    /// </summary>
    public static BackupCreationSettings DefaultValue { get; } = new();

    /// <summary>
    /// Gets the file name under which these settings are stored.
    /// </summary>
    public static string FileName => "backup-creation-settings.json";
}
