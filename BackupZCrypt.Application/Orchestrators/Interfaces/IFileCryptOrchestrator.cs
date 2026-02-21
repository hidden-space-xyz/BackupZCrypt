namespace BackupZCrypt.Application.Orchestrators.Interfaces;

using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Domain.ValueObjects.FileCrypt;

public interface IFileCryptOrchestrator
{
    Task<Result<FileCryptResult>> ExecuteAsync(
        FileCryptRequest request,
        IProgress<FileCryptStatus> progress,
        CancellationToken cancellationToken = default);
}
