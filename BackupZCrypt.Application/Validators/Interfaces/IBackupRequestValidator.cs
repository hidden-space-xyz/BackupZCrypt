using BackupZCrypt.Domain.ValueObjects.Backup;

namespace BackupZCrypt.Application.Validators.Interfaces;

public interface IBackupRequestValidator
{
    Task<IReadOnlyList<string>> AnalyzeErrorsAsync(
        BackupRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> AnalyzeWarningsAsync(
        BackupRequest request,
        CancellationToken cancellationToken = default);
}
