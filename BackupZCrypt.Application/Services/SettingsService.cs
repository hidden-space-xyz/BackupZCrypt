namespace BackupZCrypt.Application.Services;

using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Domain.Services.Interfaces;
using System.Text.Json;
using System.Text.Json.Serialization;

internal sealed class SettingsService(
    IFileOperationsService fileOperationsService,
    string? baseDirectoryPath = null) : ISettingsService
{
    private const string SettingsDirectoryName = "BackupZCrypt";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private string BaseDirectoryPath { get; } =
        baseDirectoryPath ?? Path.Combine(Path.GetTempPath(), SettingsDirectoryName);

    public string GetFilePath<T>()
        where T : class, ISettings<T> =>
        Path.GetFullPath(Path.Combine(this.BaseDirectoryPath, T.FileName));

    public async Task<T> GetOrCreateAsync<T>(CancellationToken cancellationToken = default)
        where T : class, ISettings<T>
    {
        var filePath = this.GetFilePath<T>();

        if (!fileOperationsService.FileExists(filePath))
        {
            var defaults = T.DefaultValue;
            await this.SaveAsync(defaults, cancellationToken);
            return defaults;
        }

        var rawSettings = await fileOperationsService.ReadAllBytesAsync(filePath, cancellationToken);

        try
        {
            var settings = JsonSerializer.Deserialize<T>(rawSettings, SerializerOptions);

            return settings
                ?? throw new InvalidOperationException(
                    $"Settings file '{filePath}' is empty or invalid.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Settings file '{filePath}' is invalid.", ex);
        }
    }

    public async Task SaveAsync<T>(T settings, CancellationToken cancellationToken = default)
        where T : class, ISettings<T>
    {
        ArgumentNullException.ThrowIfNull(settings);

        var filePath = this.GetFilePath<T>();
        var directoryPath = fileOperationsService.GetDirectoryName(filePath);

        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            throw new InvalidOperationException(
                $"Settings path '{filePath}' is invalid.");
        }

        await fileOperationsService.CreateDirectoryAsync(directoryPath, cancellationToken);

        var rawSettings = JsonSerializer.SerializeToUtf8Bytes(settings, SerializerOptions);

        await fileOperationsService.WriteAllBytesAsync(filePath, rawSettings, cancellationToken);
    }
}
