namespace BackupZCrypt.Application.Services.Interfaces;

using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Domain.ValueObjects.Backup;

public interface IDirectoryBackupService
{
    Task<Result<BackupResult>> ProcessAsync(
        string sourcePath,
        string destinationPath,
        BackupRequest request,
        IProgress<BackupStatus> progress,
        CancellationToken cancellationToken);
}
