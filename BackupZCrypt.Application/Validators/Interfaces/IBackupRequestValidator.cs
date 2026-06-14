using BackupZCrypt.Domain.ValueObjects.Backup;
using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Application.Validators.Interfaces;

/// <summary>
/// Validates backup requests, separating blocking errors from advisory warnings.
/// </summary>
public interface IBackupRequestValidator
{
    /// <summary>
    /// Analyzes a request for blocking errors that must prevent the operation from running.
    /// </summary>
    /// <param name="request">The backup request to validate.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The localizable errors found; empty when the request is valid.</returns>
    public Task<IReadOnlyList<LocalizableMessage>> AnalyzeErrorsAsync(
        BackupRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Analyzes a request for advisory warnings the user may choose to proceed past.
    /// </summary>
    /// <param name="request">The backup request to inspect.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The localizable warnings found; empty when there are none.</returns>
    public Task<IReadOnlyList<LocalizableMessage>> AnalyzeWarningsAsync(
        BackupRequest request,
        CancellationToken cancellationToken = default
    );
}
