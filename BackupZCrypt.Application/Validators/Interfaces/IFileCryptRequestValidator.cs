namespace BackupZCrypt.Application.Validators.Interfaces;

using BackupZCrypt.Domain.ValueObjects.FileCrypt;

public interface IFileCryptRequestValidator
{
    Task<IReadOnlyList<string>> AnalyzeErrorsAsync(
        FileCryptRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> AnalyzeWarningsAsync(
        FileCryptRequest request,
        CancellationToken cancellationToken = default);
}
