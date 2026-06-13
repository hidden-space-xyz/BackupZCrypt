using System.Text.Json;
using System.Text.Json.Serialization;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Domain.Services.Interfaces;

namespace BackupZCrypt.Application.Services;

/// <summary>
/// Persists strongly typed settings as indented JSON files under a per-user application data
/// directory, recreating defaults when a file is missing or corrupted.
/// </summary>
/// <param name="fileOperationsService">Service used to read and write settings files.</param>
/// <param name="baseDirectoryPath">An optional override for the settings directory; defaults to local application data.</param>
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

    private string BaseDirectoryPath { get; } =
        baseDirectoryPath
        ?? Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolderOption.Create
            ),
            SettingsDirectoryName
        );

    /// <summary>
    /// Resolves the absolute path of the file backing the given settings type.
    /// </summary>
    /// <typeparam name="T">The settings type whose file path is requested.</typeparam>
    /// <returns>The absolute path to the settings file.</returns>
    public string GetFilePath<T>()
        where T : class, ISettings<T> =>
        Path.GetFullPath(Path.Combine(this.BaseDirectoryPath, T.FileName));

    /// <summary>
    /// Loads the persisted settings, recreating and saving the defaults when the file is absent or corrupted.
    /// </summary>
    /// <typeparam name="T">The settings type to load.</typeparam>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The loaded settings, or freshly created defaults.</returns>
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
        }

        var recreated = T.DefaultValue;
        await this.SaveAsync(recreated, cancellationToken);
        return recreated;
    }

    /// <summary>
    /// Serializes the given settings to indented JSON and writes them to disk, creating the directory if needed.
    /// </summary>
    /// <typeparam name="T">The settings type to save.</typeparam>
    /// <param name="settings">The settings instance to persist.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the settings have been written.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="settings"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The resolved settings path has no directory component.</exception>
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
