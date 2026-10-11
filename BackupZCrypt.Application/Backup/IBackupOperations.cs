using BackupZCrypt.Application.Common;
using BackupZCrypt.Application.Manifest;

namespace BackupZCrypt.Application.Backup;

/// <summary>
/// The backup use cases a front end runs: create, update, restore, and verify an archive, and
/// inspect a folder before choosing it as one.
/// </summary>
/// <remarks>
/// Create, update, and restore validate their request and report blocking problems as a failed
/// result; warnings the user has not yet agreed to proceed past come back as an outcome awaiting
/// confirmation instead of running. Verify checks only the password and the archive path. Apart
/// from a <see langword="null"/> request and cancellation, nothing is thrown.
/// </remarks>
public interface IBackupOperations
{
    /// <summary>
    /// Creates a new archive of a source folder with the algorithms the request chooses.
    /// </summary>
    /// <param name="request">The paths, password, and algorithm choices of the new archive.</param>
    /// <param name="progress">A sink that receives incremental status updates, or <see langword="null"/> to discard them.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The outcome of the operation, or the errors that stopped it.</returns>
    public Task<Result<BackupOutcome>> CreateAsync(
        CreateBackupRequest request,
        IProgress<BackupStatus>? progress = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Updates an existing archive from the current state of its source folder.
    /// </summary>
    /// <param name="request">The source folder, the archive, and its password.</param>
    /// <param name="progress">A sink that receives incremental status updates, or <see langword="null"/> to discard them.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The outcome of the operation, or the errors that stopped it.</returns>
    public Task<Result<BackupOutcome>> UpdateAsync(
        UpdateBackupRequest request,
        IProgress<BackupStatus>? progress = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Restores an existing archive into a destination folder.
    /// </summary>
    /// <param name="request">The archive, the destination folder, and the archive's password.</param>
    /// <param name="progress">A sink that receives incremental status updates, or <see langword="null"/> to discard them.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The outcome of the operation, or the errors that stopped it.</returns>
    public Task<Result<BackupOutcome>> RestoreAsync(
        RestoreBackupRequest request,
        IProgress<BackupStatus>? progress = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Verifies the integrity of an existing archive without writing anything.
    /// </summary>
    /// <param name="request">The archive and its password.</param>
    /// <param name="progress">A sink that receives incremental status updates, or <see langword="null"/> to discard them.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The outcome of the verification, or the errors that stopped it.</returns>
    public Task<Result<BackupOutcome>> VerifyAsync(
        VerifyBackupRequest request,
        IProgress<BackupStatus>? progress = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Classifies the folder a user picked as an archive: a missing path, a file, a folder without a
    /// manifest, or a folder whose manifest is readable, damaged, or names an unsupported algorithm.
    /// </summary>
    /// <param name="backupPath">The path the user picked, as typed.</param>
    /// <param name="cancellationToken">A token to cancel the inspection.</param>
    /// <returns>The detected manifest kind; a path that cannot be read is reported rather than thrown.</returns>
    public Task<ManifestKind> DetectManifestKindAsync(
        string backupPath,
        CancellationToken cancellationToken = default
    );
}
