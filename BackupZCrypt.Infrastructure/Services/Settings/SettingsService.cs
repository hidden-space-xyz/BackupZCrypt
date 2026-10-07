using System.Text.Json;
using System.Text.Json.Serialization;


using BackupZCrypt.Domain.Services.Interfaces;

namespace BackupZCrypt.Infrastructure.Services.Settings;

/// <summary>
/// Persists strongly typed settings as indented JSON files under a per-user application data
/// directory, creating the defaults when a file is missing and falling back to them, without
/// overwriting anything, when a file cannot be read.
/// </summary>
/// <param name="fileOperationsService">The service used to read and write settings files.</param>
/// <param name="baseDirectoryPath">An optional override for the settings directory; defaults to local application data.</param>
internal sealed class SettingsService(
    IFileOperationsService fileOperationsService,
    string? baseDirectoryPath = null
) : ISettingsService
{
    /// <summary>
    /// The largest settings document accepted in memory.
    /// </summary>
    private const int MaximumSettingsFileSize = 1024 * 1024;

    /// <summary>
    /// The folder created under the per-user application data directory that holds every settings file.
    /// </summary>
    private const string SettingsDirectoryName = "BackupZCrypt";

    /// <summary>
    /// The JSON options shared by reads and writes: indented output, with enums stored as names so a settings
    /// file stays readable and survives renumbering of the enum members. Numbers are refused for enums, so a
    /// hand-edited value that names no member cannot slip through as an undefined one.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
    };

    /// <summary>
    /// The byte order mark many editors write at the start of a UTF-8 file.
    /// </summary>
    private static ReadOnlySpan<byte> Utf8ByteOrderMark => [0xEF, 0xBB, 0xBF];

    /// <summary>
    /// Gets the directory that settings files are read from and written to.
    /// </summary>
    private string BaseDirectoryPath { get; } =
        baseDirectoryPath
        ?? Path.Join(
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
        where T : class, ISettings<T>
    {
        return Path.GetFullPath(Path.Join(this.BaseDirectoryPath, T.FileName));
    }

    /// <summary>
    /// Loads the persisted settings, creating the file with the defaults when it does not exist yet.
    /// </summary>
    /// <remarks>
    /// A file that cannot be read as settings is not treated as fatal, and it is not overwritten
    /// either: the defaults are returned for this session and the file is left exactly as it was, so a
    /// hand edit with a typo can still be fixed instead of being silently discarded. The next explicit
    /// save replaces it. A UTF-8 byte order mark, which many editors add, is accepted.
    /// </remarks>
    /// <typeparam name="T">The settings type to load.</typeparam>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The loaded settings, or the defaults.</returns>
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

        byte[]? rawSettings = null;

        try
        {
            try
            {
                rawSettings = await fileOperationsService
                    .ReadAllBytesBoundedAsync(
                        filePath,
                        MaximumSettingsFileSize,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }
            catch (InvalidDataException)
            {
                return T.DefaultValue;
            }

            return TryDeserialize<T>(rawSettings) ?? T.DefaultValue;
        }
        finally
        {
            if (rawSettings is not null)
            {
                Array.Clear(rawSettings);
            }
        }
    }

    /// <summary>
    /// Deserializes persisted settings, skipping a leading byte order mark and reporting a corrupt or
    /// version-incompatible file as a missing value rather than as an exception.
    /// </summary>
    /// <typeparam name="T">The settings type to read.</typeparam>
    /// <param name="rawSettings">The raw bytes read from the settings file.</param>
    /// <returns>The deserialized settings, or <see langword="null"/> when the file cannot be read as <typeparamref name="T"/>.</returns>
    private static T? TryDeserialize<T>(byte[] rawSettings)
        where T : class, ISettings<T>
    {
        ReadOnlySpan<byte> json = rawSettings;
        if (json.StartsWith(Utf8ByteOrderMark))
        {
            json = json[Utf8ByteOrderMark.Length..];
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Serializes the given settings to indented JSON and atomically replaces the persisted file,
    /// creating the directory if needed.
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

        try
        {
            await fileOperationsService
                .WriteFileAtomicallyAsync(
                    filePath,
                    (stream, token) => stream.WriteAsync(rawSettings, token).AsTask(),
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        finally
        {
            Array.Clear(rawSettings);
        }
    }
}
