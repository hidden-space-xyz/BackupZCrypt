namespace BackupZCrypt.Application.Services.Interfaces;

using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Domain.ValueObjects.FileCrypt;

public interface IFileCryptDirectoryService
{
    Task<Result<FileCryptResult>> ProcessAsync(
        string sourcePath,
        string destinationPath,
        FileCryptRequest request,
        IProgress<FileCryptStatus> progress,
        CancellationToken cancellationToken);
}
