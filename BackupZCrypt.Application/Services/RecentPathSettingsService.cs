namespace BackupZCrypt.Application.Services;

using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects.Backup;
using BackupZCrypt.Domain.Services.Interfaces;
using System.Text.Json;

internal sealed class RecentPathSettingsService(
    IFileOperationsService fileOperationsService,
    string? settingsFilePath = null) : IRecentPathSettingsService
{
    private const string SettingsDirectoryName = "BackupZCrypt";
    private const string SettingsFileName = "recent-path-settings.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    public string SettingsFilePath { get; } = Path.GetFullPath(
        settingsFilePath
        ?? Path.Combine(Path.GetTempPath(), SettingsDirectoryName, SettingsFileName));

    public async Task<RecentPathSettings> GetOrCreateAsync(
        CancellationToken cancellationToken = default)
    {
        if (!fileOperationsService.FileExists(this.SettingsFilePath))
        {
            var defaults = RecentPathSettings.Default;
            await this.SaveAsync(defaults, cancellationToken);
            return defaults;
        }

        var rawSettings = await fileOperationsService.ReadAllBytesAsync(
            this.SettingsFilePath,
            cancellationToken);

        try
        {
            var settings = JsonSerializer.Deserialize<RecentPathSettings>(
                rawSettings,
                SerializerOptions);

            return settings
                ?? throw new InvalidOperationException(
                    $"Recent path settings file '{this.SettingsFilePath}' is empty or invalid.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Recent path settings file '{this.SettingsFilePath}' is invalid.",
                ex);
        }
    }

    public async Task SaveAsync(
        RecentPathSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directoryPath = fileOperationsService.GetDirectoryName(this.SettingsFilePath);
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            throw new InvalidOperationException(
                $"Recent path settings path '{this.SettingsFilePath}' is invalid.");
        }

        await fileOperationsService.CreateDirectoryAsync(directoryPath, cancellationToken);

        var rawSettings = JsonSerializer.SerializeToUtf8Bytes(settings, SerializerOptions);

        await fileOperationsService.WriteAllBytesAsync(
            this.SettingsFilePath,
            rawSettings,
            cancellationToken);
    }

    public async Task RememberPathsAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var settings = await this.GetOrCreateAsync(cancellationToken);

        await this.SaveAsync(
            settings with
            {
                LastSourcePath = Path.GetFullPath(sourcePath),
                LastDestinationPath = Path.GetFullPath(destinationPath),
            },
            cancellationToken);
    }
}
