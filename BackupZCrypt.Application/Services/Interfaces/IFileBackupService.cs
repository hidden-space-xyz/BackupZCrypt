using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Domain.ValueObjects.Backup;

namespace BackupZCrypt.Application.Services.Interfaces;

/// <summary>
/// Handles backup operations whose source is a single file, dispatching to the chunked backup service.
/// </summary>
public interface IFileBackupService
{
    /// <summary>
    /// Runs the requested single-file backup operation (create or restore).
    /// </summary>
    /// <param name="sourcePath">The source file path.</param>
    /// <param name="destinationPath">The destination directory path.</param>
    /// <param name="request">The backup request selecting the operation and options.</param>
    /// <param name="progress">A sink that receives incremental status updates.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A result describing the operation outcome.</returns>
    Task<Result<BackupResult>> ProcessAsync(
        string sourcePath,
        string destinationPath,
        BackupRequest request,
        IProgress<BackupStatus> progress,
        CancellationToken cancellationToken
    );
}
