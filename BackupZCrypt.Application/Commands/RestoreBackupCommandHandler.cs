using BackupZCrypt.Application.Commands.Interfaces;
using BackupZCrypt.Application.Orchestrators;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Application.ValueObjects.Backup;
using BackupZCrypt.Domain.ValueObjects.Backup;

namespace BackupZCrypt.Application.Commands;

/// <summary>
/// Handles <see cref="RestoreBackupCommand"/> by building the backup request and running the shared
/// backup pipeline for the restore operation.
/// </summary>
/// <param name="runner">The shared pipeline that validates, prepares, and executes backup operations.</param>
internal sealed class RestoreBackupCommandHandler(BackupOperationRunner runner)
    : ICommandHandler<RestoreBackupCommand, Result<BackupOutcome>>
{
    /// <summary>
    /// Executes the restore-backup operation the command describes.
    /// </summary>
    /// <param name="command">The command carrying the archive path, destination, and password.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The outcome of the operation.</returns>
    public Task<Result<BackupOutcome>> HandleAsync(
        RestoreBackupCommand command,
        CancellationToken cancellationToken = default
    )
    {
        var request = BackupRequest.ForRestore(
            command.BackupPath,
            command.DestinationPath,
            command.Password,
            command.ProceedOnWarnings
        );

        return runner.RunAsync(request, command.Progress, cancellationToken);
    }
}
