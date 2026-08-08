using BackupZCrypt.Application.Queries.Interfaces;
using BackupZCrypt.Domain.Services.Interfaces;

namespace BackupZCrypt.Application.Queries;

/// <summary>
/// Handles <see cref="GetSettingsFilePathQuery{TSettings}"/> by resolving the path through the
/// settings service.
/// </summary>
/// <typeparam name="TSettings">The settings type whose file path is requested.</typeparam>
/// <param name="settingsService">The service that persists settings on disk.</param>
internal sealed class GetSettingsFilePathQueryHandler<TSettings>(ISettingsService settingsService)
    : ISyncQueryHandler<GetSettingsFilePathQuery<TSettings>, string>
    where TSettings : class, ISettings<TSettings>
{
    /// <summary>
    /// Resolves the queried settings file path.
    /// </summary>
    /// <param name="query">The query identifying the settings type.</param>
    /// <returns>The absolute path to the settings file.</returns>
    public string Handle(GetSettingsFilePathQuery<TSettings> query)
    {
        return settingsService.GetFilePath<TSettings>();
    }
}
