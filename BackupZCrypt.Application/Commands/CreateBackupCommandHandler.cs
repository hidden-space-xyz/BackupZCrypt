using BackupZCrypt.Application.Commands.Interfaces;
using BackupZCrypt.Application.Orchestrators;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Application.ValueObjects.Backup;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.ValueObjects.Backup;

namespace BackupZCrypt.Application.Commands;

/// <summary>
/// Handles <see cref="CreateBackupCommand"/> by building the backup request and running the shared
/// backup pipeline for the create operation.
/// </summary>
/// <param name="runner">The shared pipeline that validates, prepares, and executes backup operations.</param>
internal sealed class CreateBackupCommandHandler(BackupOperationRunner runner)
    : ICommandHandler<CreateBackupCommand, Result<BackupOutcome>>
{
    /// <summary>
    /// Executes the create-backup operation the command describes.
    /// </summary>
    /// <param name="command">The command carrying the paths, password, and algorithm choices.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The outcome of the operation.</returns>
    public Task<Result<BackupOutcome>> HandleAsync(
        CreateBackupCommand command,
        CancellationToken cancellationToken = default
    )
    {
        var request = new BackupRequest(
            command.SourcePath,
            command.DestinationPath,
            command.Password,
            command.ConfirmPassword,
            command.EncryptionAlgorithm,
            command.KeyDerivationAlgorithm,
            BackupOperation.Create,
            command.Compression,
            command.ProceedOnWarnings
        );

        return runner.RunAsync(request, command.Progress, cancellationToken);
    }
}
