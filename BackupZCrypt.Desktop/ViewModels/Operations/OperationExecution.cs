using BackupZCrypt.Application.Backup;
using BackupZCrypt.Application.Common;

namespace BackupZCrypt.Desktop.ViewModels.Operations;

/// <summary>
/// Runs one operation with a request the page has already captured.
/// </summary>
/// <param name="proceedOnWarnings">Whether the operation should continue past advisory warnings.</param>
/// <param name="progress">The sink that receives incremental status updates.</param>
/// <param name="cancellationToken">A token to cancel the operation.</param>
/// <returns>The outcome of the operation.</returns>
internal delegate Task<Result<BackupOutcome>> OperationExecution(
    bool proceedOnWarnings,
    IProgress<BackupStatus> progress,
    CancellationToken cancellationToken
);
