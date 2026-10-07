using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.Utilities.Formatters;
using BackupZCrypt.Application.Utilities.Helpers;
using BackupZCrypt.Application.Validators.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Application.ValueObjects.Backup;
using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Services.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Backup;
using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Application.Orchestrators;

/// <summary>
/// Runs the shared pipeline behind the backup command and query handlers: it validates the request,
/// previews what an update or restore will do so its consequences can be confirmed, normalizes paths,
/// takes the backup's lock, and dispatches to the chunk-based backup service. Verification is
/// read-only and takes a dedicated path that skips request validation.
/// </summary>
/// <remarks>
/// Nothing in this pipeline deletes user data. A create into a folder that holds anything other than
/// a backup is refused during validation, and a backup it replaces is only pruned by the engine after
/// the new one has been published.
/// </remarks>
/// <param name="backupRequestValidator">The validator producing blocking errors and advisory warnings.</param>
/// <param name="fileOperationsService">The service used to inspect and prepare the file system.</param>
/// <param name="chunkedBackupService">The service that performs the chunk-based backup, update, restore, and verify operations.</param>
/// <param name="systemStorage">The service used to query free space for the preview warnings.</param>
internal sealed class BackupOperationRunner(
    IBackupRequestValidator backupRequestValidator,
    IFileOperationsService fileOperationsService,
    IChunkedBackupService chunkedBackupService,
    ISystemStorageService systemStorage
)
{
    /// <summary>
    /// Validates the request and, if it passes, runs the create, update, or restore operation it
    /// describes.
    /// </summary>
    /// <param name="request">The backup request describing the operation, paths, and options.</param>
    /// <param name="progress">A sink that receives incremental status updates, or <see langword="null"/> to discard them.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A successful result carrying the completed engine outcome or the warnings awaiting the user's
    /// confirmation, or a failure result carrying validation or fatal errors.
    /// </returns>
    public async Task<Result<BackupOutcome>> RunAsync(
        BackupRequest request,
        IProgress<BackupStatus>? progress,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        var validationResult = await ValidateRequestAsync(request, cancellationToken);
        if (validationResult is not null)
        {
            return validationResult;
        }

        string? createdDestination = null;
        var succeeded = false;

        try
        {
            var (sourcePath, destinationPath) = NormalizePaths(request);

            var sourceError = CheckSourceDirectory(sourcePath);
            if (sourceError is not null)
            {
                return Result<BackupOutcome>.Failure(sourceError.Value);
            }

            if (
                request.Operation is BackupOperation.Update
                && !fileOperationsService.DirectoryExists(destinationPath)
            )
            {
                return Result<BackupOutcome>.Failure(MessageCode.BackupDestinationMustExist);
            }

            if (request.Operation is BackupOperation.Create)
            {
                if (!fileOperationsService.DirectoryExists(destinationPath))
                {
                    createdDestination = destinationPath;
                }

                await fileOperationsService.CreateDirectoryAsync(destinationPath, cancellationToken);
            }

            var engineResult = await RunUnderLockAsync(
                sourcePath,
                destinationPath,
                request,
                progress ?? NullProgress<BackupStatus>.Instance,
                cancellationToken
            );

            succeeded = engineResult.IsSuccess;
            return ToOutcome(engineResult);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException) when (request.Operation is not BackupOperation.Verify)
        {
            return Result<BackupOutcome>.Failure(MessageCode.DestinationAccessDenied);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Result<BackupOutcome>.Failure(MessageCode.UnexpectedErrorFormat, ex.Message);
        }
        finally
        {
            if (!succeeded && createdDestination is not null)
            {
                TryRemoveEmptyDirectory(createdDestination);
            }
        }
    }

    /// <summary>
    /// Runs the read-only verify path, which requires a password and an existing source directory but
    /// never creates or writes to a destination.
    /// </summary>
    /// <param name="request">The backup request identifying the archive to verify.</param>
    /// <param name="progress">A sink that receives incremental status updates, or <see langword="null"/> to discard them.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A successful result carrying the completed verification outcome, or a failure result when the
    /// password is missing, the source path cannot be normalized, the source directory is absent, the
    /// backup is being modified, or an unexpected error occurs.
    /// </returns>
    public async Task<Result<BackupOutcome>> RunVerifyAsync(
        BackupRequest request,
        IProgress<BackupStatus>? progress,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            return Result<BackupOutcome>.Failure(MessageCode.PasswordRequired);
        }

        try
        {
            var sourcePath =
                PathNormalizationHelper.TryNormalize(request.SourcePath, out var normalizeError)
                ?? request.SourcePath;

            if (normalizeError is not null)
            {
                return Result<BackupOutcome>.Failure(normalizeError);
            }

            var sourceError = CheckSourceDirectory(sourcePath);
            if (sourceError is not null)
            {
                return Result<BackupOutcome>.Failure(sourceError.Value);
            }

            if (IsBackupLocked(sourcePath))
            {
                return Result<BackupOutcome>.Failure(MessageCode.BackupInUse);
            }

            var engineResult = await chunkedBackupService.VerifyAsync(
                sourcePath,
                request,
                progress ?? NullProgress<BackupStatus>.Instance,
                cancellationToken
            );

            return ToOutcome(engineResult);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Result<BackupOutcome>.Failure(MessageCode.UnexpectedErrorFormat, ex.Message);
        }
    }

    /// <summary>
    /// Takes the backup's lock for the operation and dispatches it. A create or update holds the
    /// lock exclusively for its whole run; a restore only checks that no create or update holds it.
    /// </summary>
    /// <param name="sourcePath">The normalized absolute source path.</param>
    /// <param name="destinationPath">The normalized absolute destination path.</param>
    /// <param name="request">The backup request describing the operation, paths, and options.</param>
    /// <param name="progress">A sink that receives incremental status updates.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The outcome reported by the engine, or a failure when the backup is in use.</returns>
    private async Task<Result<BackupResult>> RunUnderLockAsync(
        string sourcePath,
        string destinationPath,
        BackupRequest request,
        IProgress<BackupStatus> progress,
        CancellationToken cancellationToken
    )
    {
        if (request.Operation is BackupOperation.Restore)
        {
            return IsBackupLocked(sourcePath)
                ? Result<BackupResult>.Failure(MessageCode.BackupInUse)
                : await RunBackupAsync(sourcePath, destinationPath, request, progress, cancellationToken);
        }

        IDisposable backupLock;
        try
        {
            backupLock = fileOperationsService.AcquireExclusiveLock(
                fileOperationsService.CombinePath(destinationPath, BackupConstants.LockFileName)
            );
        }
        catch (IOException)
        {
            return Result<BackupResult>.Failure(MessageCode.BackupInUse);
        }

        using (backupLock)
        {
            return await RunBackupAsync(sourcePath, destinationPath, request, progress, cancellationToken);
        }
    }

    /// <summary>
    /// Dispatches an already-validated request to the create, update, or restore path of the
    /// chunk-based backup service.
    /// </summary>
    /// <param name="sourcePath">The normalized absolute source path.</param>
    /// <param name="destinationPath">The normalized absolute destination path.</param>
    /// <param name="request">The backup request describing the operation, paths, and options.</param>
    /// <param name="progress">A sink that receives incremental status updates.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The outcome reported by the selected backup service operation.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="request"/> carries an operation this method does not dispatch, such as
    /// <see cref="BackupOperation.Verify"/>, which is handled on its own read-only path.
    /// </exception>
    private Task<Result<BackupResult>> RunBackupAsync(
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
            BackupOperation.Verify => throw new ArgumentOutOfRangeException(nameof(request)),
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };
    }

    /// <summary>
    /// Determines whether a create or update currently holds the backup in a folder.
    /// </summary>
    /// <param name="backupPath">The backup folder.</param>
    /// <returns><see langword="true"/> when the backup is being modified.</returns>
    private bool IsBackupLocked(string backupPath)
    {
        return fileOperationsService.IsLockHeld(
            fileOperationsService.CombinePath(backupPath, BackupConstants.LockFileName)
        );
    }

    /// <summary>
    /// Removes a destination folder this run created when the run left it empty.
    /// </summary>
    /// <param name="directoryPath">The folder the run created.</param>
    private void TryRemoveEmptyDirectory(string directoryPath)
    {
        try
        {
            if (
                fileOperationsService.DirectoryExists(directoryPath)
                && fileOperationsService.GetDirectoryEntryNames(directoryPath).Count is 0
            )
            {
                fileOperationsService.DeleteEmptyDirectory(directoryPath);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }
    }

    /// <summary>
    /// Checks that the source path is an existing directory, distinguishing a path that is a file
    /// from one that does not exist at all.
    /// </summary>
    /// <param name="sourcePath">The normalized source path to check.</param>
    /// <returns>The message code describing the problem, or <see langword="null"/> when the path is a directory.</returns>
    private MessageCode? CheckSourceDirectory(string sourcePath)
    {
        return fileOperationsService.DirectoryExists(sourcePath)
            ? null
            : ClassifyMissingSourceDirectory(sourcePath);
    }

    /// <summary>
    /// Classifies a source path that is known not to be a directory, distinguishing one that points at
    /// an existing file from one that does not exist at all.
    /// </summary>
    /// <param name="sourcePath">The normalized source path that failed the directory check.</param>
    /// <returns>The message code describing why the path cannot be used as a source directory.</returns>
    private MessageCode ClassifyMissingSourceDirectory(string sourcePath)
    {
        return fileOperationsService.FileExists(sourcePath)
            ? MessageCode.SourceMustBeDirectory
            : MessageCode.SourcePathNotExist;
    }

    /// <summary>
    /// Expands and resolves the request's source and destination paths, keeping the raw values when
    /// normalization fails so the caller's existence checks report the problem instead.
    /// </summary>
    /// <param name="request">The backup request whose paths are normalized.</param>
    /// <returns>The normalized source and destination paths.</returns>
    private static (string SourcePath, string DestinationPath) NormalizePaths(BackupRequest request)
    {
        var sourcePath =
            PathNormalizationHelper.TryNormalize(request.SourcePath, out _) ?? request.SourcePath;

        var destinationPath =
            PathNormalizationHelper.TryNormalize(request.DestinationPath, out _)
            ?? request.DestinationPath;

        return (sourcePath, destinationPath);
    }

    /// <summary>
    /// Runs the request validator and, unless the user already agreed to proceed, the preview of an
    /// update or restore. Blocking errors become a failure, and warnings the user has not agreed to
    /// proceed past become an outcome awaiting their confirmation.
    /// </summary>
    /// <param name="request">The backup request to validate.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A result carrying the validation errors or pending warnings that stop the operation, or
    /// <see langword="null"/> when the request may proceed.
    /// </returns>
    private async Task<Result<BackupOutcome>?> ValidateRequestAsync(
        BackupRequest request,
        CancellationToken cancellationToken
    )
    {
        var errors = await backupRequestValidator.AnalyzeErrorsAsync(request, cancellationToken);
        if (errors.Count > 0)
        {
            return Result<BackupOutcome>.Failure([.. errors]);
        }

        if (request.ProceedOnWarnings)
        {
            return null;
        }

        List<LocalizableMessage> warnings =
        [
            .. await backupRequestValidator.AnalyzeWarningsAsync(request, cancellationToken),
        ];

        var preview = await PreviewAsync(request, cancellationToken);
        if (preview is { IsSuccess: false })
        {
            return Result<BackupOutcome>.Failure([.. preview.Errors]);
        }

        if (preview is { IsSuccess: true })
        {
            warnings.AddRange(BuildPreviewWarnings(request, preview.Value));
        }

        return warnings.Count > 0
            ? Result<BackupOutcome>.Success(BackupOutcome.AwaitingConfirmation(warnings))
            : null;
    }

    /// <summary>
    /// Opens the backup an update or restore will use and compares it with what the operation is
    /// about to do. A wrong password or a missing manifest is reported here, before anything is
    /// created or written.
    /// </summary>
    /// <param name="request">The request to preview.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The preview, or <see langword="null"/> for an operation that has none or when it could not run.</returns>
    private async Task<Result<BackupPreview>?> PreviewAsync(
        BackupRequest request,
        CancellationToken cancellationToken
    )
    {
        var (sourcePath, destinationPath) = NormalizePaths(request);

        try
        {
            return request.Operation switch
            {
                BackupOperation.Update => await chunkedBackupService.PreviewUpdateAsync(
                    sourcePath,
                    destinationPath,
                    request.Password,
                    cancellationToken
                ),
                BackupOperation.Restore => await chunkedBackupService.PreviewRestoreAsync(
                    sourcePath,
                    request.Password,
                    cancellationToken
                ),
                BackupOperation.Create or BackupOperation.Verify => null,
                _ => null,
            };
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException and not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>
    /// Turns a preview into the warnings the user confirms: the files an update removes from the
    /// backup, a source that shares nothing with the backup, and too little free space for the data
    /// the operation will write.
    /// </summary>
    /// <param name="request">The previewed request.</param>
    /// <param name="preview">The preview of the operation.</param>
    /// <returns>The warnings to confirm.</returns>
    private List<LocalizableMessage> BuildPreviewWarnings(BackupRequest request, BackupPreview preview)
    {
        List<LocalizableMessage> warnings = [];
        var (_, destinationPath) = NormalizePaths(request);
        long requiredSpace;

        if (request.Operation is BackupOperation.Update)
        {
            if (preview.FileCount > 0 && preview.MatchedFiles is 0)
            {
                warnings.Add(
                    new LocalizableMessage(MessageCode.UpdateSourceMismatchFormat, preview.FileCount)
                );
            }
            else if (preview.RemovedPaths.Count > 0)
            {
                warnings.Add(
                    new LocalizableMessage(
                        MessageCode.UpdateRemovesFilesFormat,
                        preview.RemovedPaths.Count,
                        PathSample.Join(preview.RemovedPaths)
                    )
                );
            }

            requiredSpace = (long)(preview.BytesToProcess * 1.2);
        }
        else
        {
            requiredSpace = preview.TotalBytes;
        }

        var drive = systemStorage.GetPathRoot(destinationPath);
        if (string.IsNullOrEmpty(drive) || !systemStorage.IsDriveReady(drive))
        {
            return warnings;
        }

        var available = systemStorage.GetAvailableFreeSpace(drive);
        if (available >= 0 && available < requiredSpace)
        {
            warnings.Add(
                new LocalizableMessage(
                    MessageCode.LowDiskSpaceFormat,
                    ByteSizeFormatter.Format(available),
                    ByteSizeFormatter.Format(requiredSpace)
                )
            );
        }

        return warnings;
    }

    /// <summary>
    /// Maps the engine's result onto the handler contract, preserving the failure channel and wrapping
    /// a completed run — fully or partially successful — as a completed outcome.
    /// </summary>
    /// <param name="engineResult">The result reported by the chunk-based backup service.</param>
    /// <returns>The mapped result.</returns>
    private static Result<BackupOutcome> ToOutcome(Result<BackupResult> engineResult)
    {
        return engineResult.IsSuccess
            ? Result<BackupOutcome>.Success(BackupOutcome.Completed(engineResult.Value))
            : Result<BackupOutcome>.Failure([.. engineResult.Errors]);
    }
}
