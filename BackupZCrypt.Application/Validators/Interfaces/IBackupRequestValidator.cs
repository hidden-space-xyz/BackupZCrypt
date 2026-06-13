using BackupZCrypt.Domain.ValueObjects.Backup;
using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Application.Validators.Interfaces;

public interface IBackupRequestValidator
{
    Task<IReadOnlyList<LocalizableMessage>> AnalyzeErrorsAsync(
        BackupRequest request,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyList<LocalizableMessage>> AnalyzeWarningsAsync(
        BackupRequest request,
        CancellationToken cancellationToken = default
    );
}
