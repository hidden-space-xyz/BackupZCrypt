using BackupZCrypt.Application.Commands.Interfaces;
using BackupZCrypt.Application.Orchestrators;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Application.ValueObjects.Backup;
using BackupZCrypt.Domain.ValueObjects.Backup;

namespace BackupZCrypt.Application.Commands;

/// <summary>
/// Handles <see cref="UpdateBackupCommand"/> by building the backup request and running the shared
/// backup pipeline for the update operation.
/// </summary>
/// <param name="runner">The shared pipeline that validates, prepares, and executes backup operations.</param>
internal sealed class UpdateBackupCommandHandler(BackupOperationRunner runner)
    : ICommandHandler<UpdateBackupCommand, Result<BackupOutcome>>
{
    /// <summary>
    /// Executes the update-backup operation the command describes.
    /// </summary>
    /// <param name="command">The command carrying the source directory, archive path, and password.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The outcome of the operation.</returns>
    public Task<Result<BackupOutcome>> HandleAsync(
        UpdateBackupCommand command,
        CancellationToken cancellationToken = default
    )
    {
        var request = BackupRequest.ForUpdate(
            command.SourcePath,
            command.BackupPath,
            command.Password,
            command.ProceedOnWarnings
        );

        return runner.RunAsync(request, command.Progress, cancellationToken);
    }
}
