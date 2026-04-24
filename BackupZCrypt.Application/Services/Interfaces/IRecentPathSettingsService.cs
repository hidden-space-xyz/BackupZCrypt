namespace BackupZCrypt.Application.Services.Interfaces;

using BackupZCrypt.Application.ValueObjects.Backup;

public interface IRecentPathSettingsService
{
    string SettingsFilePath { get; }

    Task<RecentPathSettings> GetOrCreateAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(
        RecentPathSettings settings,
        CancellationToken cancellationToken = default);

    Task RememberPathsAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default);
}
