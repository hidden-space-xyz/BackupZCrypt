using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Domain.ValueObjects.Backup;

namespace BackupZCrypt.Application.Orchestrators.Interfaces;

/// <summary>
/// Coordinates a backup, update, or restore operation end to end, including validation,
/// destination preparation, and dispatch to the appropriate backup service.
/// </summary>
public interface IBackupOrchestrator
{
    /// <summary>
    /// Validates the request and runs the requested backup operation, reporting progress as it proceeds.
    /// </summary>
    /// <param name="request">The backup request describing the operation, paths, and options.</param>
    /// <param name="progress">A sink that receives incremental status updates.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A result wrapping the backup outcome, or validation errors and warnings.</returns>
    public Task<Result<BackupResult>> ExecuteAsync(
        BackupRequest request,
        IProgress<BackupStatus> progress,
        CancellationToken cancellationToken = default
    );
}
