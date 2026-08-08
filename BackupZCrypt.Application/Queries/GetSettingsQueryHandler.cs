using BackupZCrypt.Application.Queries.Interfaces;
using BackupZCrypt.Domain.Services.Interfaces;

namespace BackupZCrypt.Application.Queries;

/// <summary>
/// Handles <see cref="GetSettingsQuery{TSettings}"/> by loading the persisted settings, absorbing
/// storage failures into the type's defaults.
/// </summary>
/// <remarks>
/// The defaults are already the documented answer when no valid file exists, so a load that throws —
/// a locked file, a permission error — is reported as exactly that rather than surfacing an
/// exception every caller would map to the same fallback anyway.
/// </remarks>
/// <typeparam name="TSettings">The settings type to load.</typeparam>
/// <param name="settingsService">The service that persists settings on disk.</param>
internal sealed class GetSettingsQueryHandler<TSettings>(ISettingsService settingsService)
    : IQueryHandler<GetSettingsQuery<TSettings>, TSettings>
    where TSettings : class, ISettings<TSettings>
{
    /// <summary>
    /// Loads the queried settings.
    /// </summary>
    /// <param name="query">The query identifying the settings type.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The stored settings, or the type's defaults when loading fails.</returns>
    public async Task<TSettings> HandleAsync(
        GetSettingsQuery<TSettings> query,
        CancellationToken cancellationToken = default
    )
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
}
