namespace BackupZCrypt.Application.Services;

using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects.Backup;
using BackupZCrypt.Domain.Services.Interfaces;
using System.Text.Json;
using System.Text.Json.Serialization;

internal sealed class BackupCreationSettingsService(
    IFileOperationsService fileOperationsService,
    string? settingsFilePath = null) : IBackupCreationSettingsService
{
    private const string SettingsDirectoryName = "BackupZCrypt";
    private const string SettingsFileName = "backup-creation-settings.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter(),
        },
    };

    public string SettingsFilePath { get; } = Path.GetFullPath(
        settingsFilePath
        ?? Path.Combine(Path.GetTempPath(), SettingsDirectoryName, SettingsFileName));

    public async Task<BackupCreationSettings> GetOrCreateAsync(
        CancellationToken cancellationToken = default)
    {
        if (!fileOperationsService.FileExists(this.SettingsFilePath))
        {
            BackupCreationSettings defaults = BackupCreationSettings.Default;
            await this.SaveAsync(defaults, cancellationToken);
            return defaults;
        }

        byte[] rawSettings = await fileOperationsService.ReadAllBytesAsync(
            this.SettingsFilePath,
            cancellationToken);

        try
        {
            BackupCreationSettings? settings = JsonSerializer.Deserialize<BackupCreationSettings>(
                rawSettings,
                SerializerOptions);

            return settings
                ?? throw new InvalidOperationException(
                    $"Backup creation settings file '{this.SettingsFilePath}' is empty or invalid.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Backup creation settings file '{this.SettingsFilePath}' is invalid.",
                ex);
        }
    }

    public async Task SaveAsync(
        BackupCreationSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        string? directoryPath = fileOperationsService.GetDirectoryName(this.SettingsFilePath);
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            throw new InvalidOperationException(
                $"Backup creation settings path '{this.SettingsFilePath}' is invalid.");
        }

        await fileOperationsService.CreateDirectoryAsync(directoryPath, cancellationToken);

        byte[] rawSettings = JsonSerializer.SerializeToUtf8Bytes(settings, SerializerOptions);

        await fileOperationsService.WriteAllBytesAsync(
            this.SettingsFilePath,
            rawSettings,
            cancellationToken);
    }
}
