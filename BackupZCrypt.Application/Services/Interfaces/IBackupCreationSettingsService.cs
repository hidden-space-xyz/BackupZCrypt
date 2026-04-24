namespace BackupZCrypt.Application.Services.Interfaces;

using BackupZCrypt.Application.ValueObjects.Backup;

public interface IBackupCreationSettingsService
{
    string SettingsFilePath { get; }

    Task<BackupCreationSettings> GetOrCreateAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(
        BackupCreationSettings settings,
        CancellationToken cancellationToken = default);
}
