using BackupZCrypt.Application.Commands.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Domain.Services.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Application.Commands;

/// <summary>
/// Handles <see cref="SaveSettingsCommand{TSettings}"/> by persisting the settings and mapping
/// storage failures onto the result contract instead of letting them escape as exceptions.
/// </summary>
/// <typeparam name="TSettings">The settings type to save.</typeparam>
/// <param name="settingsService">The service that persists settings on disk.</param>
internal sealed class SaveSettingsCommandHandler<TSettings>(ISettingsService settingsService)
    : ICommandHandler<SaveSettingsCommand<TSettings>, Result>
    where TSettings : class, ISettings<TSettings>
{
    /// <summary>
    /// Persists the command's settings.
    /// </summary>
    /// <param name="command">The command carrying the settings to save.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A successful result, or a failure describing why the save did not complete.</returns>
    public async Task<Result> HandleAsync(
        SaveSettingsCommand<TSettings> command,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            await settingsService.SaveAsync(command.Settings, cancellationToken);

            return Result.Success();
        }
        catch (Exception exception)
            when (exception is not OutOfMemoryException and not OperationCanceledException)
        {
            return Result.Failure(MessageCode.UnexpectedErrorFormat, exception.Message);
        }
    }
}
