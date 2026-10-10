using BackupZCrypt.Application.Common;
using BackupZCrypt.Domain.Services.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Application.Settings;

/// <summary>
/// Loads and saves settings through <see cref="ISettingsService"/>, turning a failure to load into
/// the defaults and a failure to save into a result the caller reports.
/// </summary>
/// <param name="settingsService">The service that reads and writes settings files.</param>
internal sealed class SettingsStore(ISettingsService settingsService) : ISettingsStore
{
    /// <inheritdoc/>
    public async Task<TSettings> LoadAsync<TSettings>(CancellationToken cancellationToken = default)
        where TSettings : class, ISettings<TSettings>
    {
        try
        {
            return await settingsService.GetOrCreateAsync<TSettings>(cancellationToken);
        }
        catch (Exception exception)
            when (exception is not OutOfMemoryException and not OperationCanceledException)
        {
            return TSettings.DefaultValue;
        }
    }

    /// <inheritdoc/>
    public async Task<Result> SaveAsync<TSettings>(
        TSettings settings,
        CancellationToken cancellationToken = default
    )
        where TSettings : class, ISettings<TSettings>
    {
        try
        {
            await settingsService.SaveAsync(settings, cancellationToken);

            return Result.Success();
        }
        catch (Exception exception)
            when (exception is not OutOfMemoryException and not OperationCanceledException)
        {
            return Result.Failure(MessageCode.UnexpectedErrorFormat, exception.Message);
        }
    }

    /// <inheritdoc/>
    public string GetFilePath<TSettings>()
        where TSettings : class, ISettings<TSettings>
    {
        return settingsService.GetFilePath<TSettings>();
    }
}
