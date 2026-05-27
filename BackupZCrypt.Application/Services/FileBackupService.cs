using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.ValueObjects.Backup;

namespace BackupZCrypt.Application.Services;

internal sealed class FileBackupService(IChunkedBackupService chunkedBackupService)
    : IFileBackupService
{
    public Task<Result<BackupResult>> ProcessAsync(
        string sourcePath,
        string destinationPath,
        BackupRequest request,
        IProgress<BackupStatus> progress,
        CancellationToken cancellationToken
    )
    {
        return request.Operation switch
        {
            BackupOperation.Create => chunkedBackupService.CreateAsync(
                sourcePath,
                destinationPath,
                request,
                progress,
                cancellationToken
            ),
            BackupOperation.Restore => chunkedBackupService.RestoreAsync(
                sourcePath,
                destinationPath,
                request,
                progress,
                cancellationToken
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };
    }
}
