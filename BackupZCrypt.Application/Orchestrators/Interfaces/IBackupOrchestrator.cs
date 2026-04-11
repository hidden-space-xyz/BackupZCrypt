namespace BackupZCrypt.Application.Orchestrators.Interfaces;

using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Domain.ValueObjects.Backup;

public interface IBackupOrchestrator
{
    Task<Result<BackupResult>> ExecuteAsync(
        BackupRequest request,
        IProgress<BackupStatus> progress,
        CancellationToken cancellationToken = default);
}
