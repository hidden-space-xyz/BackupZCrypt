using System.Collections.Concurrent;

using BackupZCrypt.Application.Common;
using BackupZCrypt.Application.Settings;

namespace BackupZCrypt.Test.Common;

/// <summary>
/// A settings store kept in memory, so UI tests never read or write the user's real settings files.
/// </summary>
internal sealed class InMemorySettingsStore : ISettingsStore
{
    /// <summary>
    /// The settings saved or seeded so far, keyed by settings type.
    /// </summary>
    private readonly ConcurrentDictionary<Type, object> saved = new();

    /// <summary>
    /// Gets or sets the exception <see cref="LoadAsync{TSettings}"/> throws, or <see langword="null"/> to load normally.
    /// </summary>
    public Exception? LoadFailure { get; set; }

    /// <summary>
    /// Seeds the value later loads of the same settings type return.
    /// </summary>
    /// <typeparam name="TSettings">The settings type.</typeparam>
    /// <param name="settings">The value to return.</param>
    public void Seed<TSettings>(TSettings settings)
        where TSettings : class, ISettings<TSettings>
    {
        saved[typeof(TSettings)] = settings;
    }

    /// <inheritdoc/>
    public Task<TSettings> LoadAsync<TSettings>(CancellationToken cancellationToken = default)
        where TSettings : class, ISettings<TSettings>
    {
        return LoadFailure is { } failure
            ? Task.FromException<TSettings>(failure)
            : Task.FromResult(
                saved.TryGetValue(typeof(TSettings), out var value)
                    ? (TSettings)value
                    : TSettings.DefaultValue
            );
    }

    /// <inheritdoc/>
    public Task<Result> SaveAsync<TSettings>(
        TSettings settings,
        CancellationToken cancellationToken = default
    )
        where TSettings : class, ISettings<TSettings>
    {
        ArgumentNullException.ThrowIfNull(settings);

        saved[typeof(TSettings)] = settings;
        return Task.FromResult(Result.Success());
    }

    /// <inheritdoc/>
    public string GetFilePath<TSettings>()
        where TSettings : class, ISettings<TSettings>
    {
        return TSettings.FileName;
    }
}
