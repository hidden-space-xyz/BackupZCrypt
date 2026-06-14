using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Domain.ValueObjects.Backup;

namespace BackupZCrypt.Application.Services.Interfaces;

/// <summary>
/// Performs chunk-based backup operations: splitting files into content-defined chunks
/// that are individually compressed, encrypted, and recorded in an encrypted manifest.
/// </summary>
public interface IChunkedBackupService
{
    /// <summary>
    /// Creates a new chunked backup of the source at the destination.
    /// </summary>
    /// <param name="sourcePath">The file or directory to back up.</param>
    /// <param name="destinationPath">The directory where chunks and the manifest are written.</param>
    /// <param name="request">The backup request carrying the password and algorithm choices.</param>
    /// <param name="progress">A sink that receives incremental status updates.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A result describing the backup outcome.</returns>
    public Task<Result<BackupResult>> CreateAsync(
        string sourcePath,
        string destinationPath,
        BackupRequest request,
        IProgress<BackupStatus> progress,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Updates an existing chunked backup, re-chunking only changed files and pruning orphaned chunks.
    /// </summary>
    /// <param name="sourcePath">The source directory whose current state is compared against the backup.</param>
    /// <param name="destinationPath">The directory containing the existing backup to update.</param>
    /// <param name="request">The backup request carrying the password used to open the manifest.</param>
    /// <param name="progress">A sink that receives incremental status updates.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A result describing the update outcome.</returns>
    public Task<Result<BackupResult>> UpdateAsync(
        string sourcePath,
        string destinationPath,
        BackupRequest request,
        IProgress<BackupStatus> progress,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Restores files from a chunked backup, verifying integrity against the manifest.
    /// </summary>
    /// <param name="sourcePath">The directory containing the backup chunks and manifest.</param>
    /// <param name="destinationPath">The directory into which files are reconstructed.</param>
    /// <param name="request">The backup request carrying the password used to decrypt the manifest.</param>
    /// <param name="progress">A sink that receives incremental status updates.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A result describing the restore outcome.</returns>
    public Task<Result<BackupResult>> RestoreAsync(
        string sourcePath,
        string destinationPath,
        BackupRequest request,
        IProgress<BackupStatus> progress,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Verifies the integrity of a chunked backup without writing any files: it decrypts the
    /// manifest, then decrypts, authenticates, and re-hashes every chunk of every file, reporting
    /// any file whose chunks are missing, corrupted, or do not match the manifest.
    /// </summary>
    /// <param name="sourcePath">The directory containing the backup chunks and manifest.</param>
    /// <param name="request">The backup request carrying the password used to decrypt the manifest.</param>
    /// <param name="progress">A sink that receives incremental status updates.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A result whose value reports how many files verified successfully and any integrity errors.</returns>
    public Task<Result<BackupResult>> VerifyAsync(
        string sourcePath,
        BackupRequest request,
        IProgress<BackupStatus> progress,
        CancellationToken cancellationToken
    );
}
