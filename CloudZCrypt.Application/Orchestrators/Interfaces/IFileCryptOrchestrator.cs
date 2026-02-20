namespace CloudZCrypt.Application.Orchestrators.Interfaces;

using CloudZCrypt.Application.ValueObjects;
using CloudZCrypt.Domain.ValueObjects.FileCrypt;

public interface IFileCryptOrchestrator
{
    Task<Result<FileCryptResult>> ExecuteAsync(
        FileCryptRequest request,
        IProgress<FileCryptStatus> progress,
        CancellationToken cancellationToken = default);
}
