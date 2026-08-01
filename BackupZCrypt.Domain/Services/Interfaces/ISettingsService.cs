namespace BackupZCrypt.Domain.Services.Interfaces;

/// <summary>
/// Persists and retrieves strongly typed application settings as JSON files on disk.
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Resolves the absolute path of the file backing the given settings type.
    /// </summary>
    /// <typeparam name="T">The settings type whose file path is requested.</typeparam>
    /// <returns>The absolute path to the settings file.</returns>
    public string GetFilePath<T>()
        where T : class, ISettings<T>;

    /// <summary>
    /// Loads the persisted settings, creating and saving the defaults if no valid file exists.
    /// </summary>
    /// <typeparam name="T">The settings type to load.</typeparam>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The loaded settings, or freshly created defaults.</returns>
    public Task<T> GetOrCreateAsync<T>(CancellationToken cancellationToken = default)
        where T : class, ISettings<T>;

    /// <summary>
    /// Serializes and writes the given settings to disk.
    /// </summary>
    /// <typeparam name="T">The settings type to save.</typeparam>
    /// <param name="settings">The settings instance to persist.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the settings have been written.</returns>
    public Task SaveAsync<T>(T settings, CancellationToken cancellationToken = default)
        where T : class, ISettings<T>;
}
