using BackupZCrypt.Application.Orchestrators;
using BackupZCrypt.Application.Queries.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Application.ValueObjects.Backup;
using BackupZCrypt.Domain.ValueObjects.Backup;

namespace BackupZCrypt.Application.Queries;

/// <summary>
/// Handles <see cref="VerifyBackupQuery"/> by building the backup request and running the shared
/// pipeline's read-only verify path.
/// </summary>
/// <param name="runner">The shared pipeline that validates, prepares, and executes backup operations.</param>
internal sealed class VerifyBackupQueryHandler(BackupOperationRunner runner)
    : IQueryHandler<VerifyBackupQuery, Result<BackupOutcome>>
{
    /// <summary>
    /// Executes the read-only verification the query describes.
    /// </summary>
    /// <param name="query">The query carrying the archive path and password.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The outcome of the verification.</returns>
    public Task<Result<BackupOutcome>> HandleAsync(
        VerifyBackupQuery query,
        CancellationToken cancellationToken = default
    )
    {
        var request = BackupRequest.ForVerify(query.BackupPath, query.Password, proceedOnWarnings: false);

        return runner.RunVerifyAsync(request, query.Progress, cancellationToken);
    }
}
