using BackupZCrypt.Application.Common;

namespace BackupZCrypt.Application.Settings;

/// <summary>
/// The settings a front end loads and saves, with failures turned into defaults or results so a
/// settings file that cannot be read or written never breaks the application.
/// </summary>
public interface ISettingsStore
{
    /// <summary>
    /// Loads the persisted settings of a type, falling back to its defaults when they cannot be read.
    /// </summary>
    /// <typeparam name="TSettings">The settings type to load.</typeparam>
    /// <param name="cancellationToken">A token to cancel the load.</param>
    /// <returns>The persisted settings, or <see cref="ISettings{TSelf}.DefaultValue"/>.</returns>
    public Task<TSettings> LoadAsync<TSettings>(CancellationToken cancellationToken = default)
        where TSettings : class, ISettings<TSettings>;

    /// <summary>
    /// Persists the settings of a type.
    /// </summary>
    /// <typeparam name="TSettings">The settings type to save.</typeparam>
    /// <param name="settings">The settings to persist.</param>
    /// <param name="cancellationToken">A token to cancel the save.</param>
    /// <returns>A success, or a failure carrying the reason the settings could not be written.</returns>
    public Task<Result> SaveAsync<TSettings>(
        TSettings settings,
        CancellationToken cancellationToken = default
    )
        where TSettings : class, ISettings<TSettings>;

    /// <summary>
    /// Gets the path of the file the settings of a type are stored in, for display.
    /// </summary>
    /// <typeparam name="TSettings">The settings type whose file is requested.</typeparam>
    /// <returns>The absolute path of the settings file.</returns>
    public string GetFilePath<TSettings>()
        where TSettings : class, ISettings<TSettings>;
}
