using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.ValueObjects.Backup;

namespace BackupZCrypt.Application.Services;

/// <summary>
/// Dispatches directory backup requests to the chunked backup service based on the requested operation.
/// </summary>
/// <param name="chunkedBackupService">The chunked backup service that performs the actual work.</param>
internal sealed class DirectoryBackupService(IChunkedBackupService chunkedBackupService)
    : IDirectoryBackupService
{
    /// <summary>
    /// Routes the request to the chunked backup service's create, update, or restore method.
    /// </summary>
    /// <param name="sourcePath">The source directory path.</param>
    /// <param name="destinationPath">The destination directory path.</param>
    /// <param name="request">The backup request selecting the operation and options.</param>
    /// <param name="progress">A sink that receives incremental status updates.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A result describing the operation outcome.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The request specifies an unsupported operation.</exception>
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
            BackupOperation.Update => chunkedBackupService.UpdateAsync(
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
