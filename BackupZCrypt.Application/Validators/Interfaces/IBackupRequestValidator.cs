namespace BackupZCrypt.Application.Validators.Interfaces;

using BackupZCrypt.Domain.ValueObjects.Backup;

public interface IBackupRequestValidator
{
    Task<IReadOnlyList<string>> AnalyzeErrorsAsync(
        BackupRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> AnalyzeWarningsAsync(
        BackupRequest request,
        CancellationToken cancellationToken = default);
}
