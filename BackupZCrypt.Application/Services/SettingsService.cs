using System.Text.Json;
using System.Text.Json.Serialization;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Domain.Services.Interfaces;

namespace BackupZCrypt.Application.Services;

internal sealed class SettingsService(
    IFileOperationsService fileOperationsService,
    string? baseDirectoryPath = null
) : ISettingsService
{
    private const string SettingsDirectoryName = "BackupZCrypt";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    // LocalApplicationData instead of the temp directory: temp may be purged at any
    // time and is world-readable on some platforms, while settings include recent
    // backup paths.
    private string BaseDirectoryPath { get; } =
        baseDirectoryPath
        ?? Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolderOption.Create
            ),
            SettingsDirectoryName
        );

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

        var rawSettings = await fileOperationsService.ReadAllBytesAsync(
            filePath,
            cancellationToken
        );

        try
        {
            var settings = JsonSerializer.Deserialize<T>(rawSettings, SerializerOptions);

            if (settings is not null)
            {
                return settings;
            }
        }
        catch (JsonException)
        {
            // Fall through to self-healing below.
        }

        // A corrupted settings file must not block the application: recreate defaults.
        var recreated = T.DefaultValue;
        await this.SaveAsync(recreated, cancellationToken);
        return recreated;
    }

    public async Task SaveAsync<T>(T settings, CancellationToken cancellationToken = default)
        where T : class, ISettings<T>
    {
        ArgumentNullException.ThrowIfNull(settings);

        var filePath = this.GetFilePath<T>();
        var directoryPath = fileOperationsService.GetDirectoryName(filePath);

        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            throw new InvalidOperationException($"Settings path '{filePath}' is invalid.");
        }

        await fileOperationsService.CreateDirectoryAsync(directoryPath, cancellationToken);

        var rawSettings = JsonSerializer.SerializeToUtf8Bytes(settings, SerializerOptions);

        await fileOperationsService.WriteAllBytesAsync(filePath, rawSettings, cancellationToken);
    }
}
