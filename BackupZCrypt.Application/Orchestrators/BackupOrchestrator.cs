using BackupZCrypt.Application.Orchestrators.Interfaces;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.Utilities.Helpers;
using BackupZCrypt.Application.Validators.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Services.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Backup;
using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Application.Orchestrators;

/// <summary>
/// Orchestrates a backup, update, restore, or verify: it validates the request, normalizes paths,
/// prepares the destination directory, and dispatches to the chunk-based backup service. Verification
/// is read-only and takes a dedicated path that skips both request validation and destination
/// preparation.
/// </summary>
/// <param name="backupRequestValidator">The validator producing blocking errors and advisory warnings.</param>
/// <param name="fileOperationsService">The service used to inspect and prepare the file system.</param>
/// <param name="chunkedBackupService">The service that performs the chunk-based backup, update, restore, and verify operations.</param>
internal sealed class BackupOrchestrator(
    IBackupRequestValidator backupRequestValidator,
    IFileOperationsService fileOperationsService,
    IChunkedBackupService chunkedBackupService
) : IBackupOrchestrator
{
    /// <summary>
    /// Validates the request and, if it passes, runs the requested backup operation.
    /// </summary>
    /// <param name="request">The backup request describing the operation, paths, and options.</param>
    /// <param name="progress">A sink that receives incremental status updates.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A successful result whose value reports the outcome (including validation errors or warnings
    /// surfaced as a non-success <see cref="BackupResult"/>), or a failure result for fatal errors.
    /// </returns>
    public async Task<Result<BackupResult>> ExecuteAsync(
        BackupRequest request,
        IProgress<BackupStatus> progress,
        CancellationToken cancellationToken = default
    )
    {
        if (request.Operation is BackupOperation.Verify)
        {
            return await ExecuteVerifyAsync(request, progress, cancellationToken);
        }

        var validationResult = await ValidateRequestAsync(request, cancellationToken);
        if (validationResult is not null)
        {
            return validationResult;
        }

        try
        {
            var (sourcePath, destinationPath) = NormalizePaths(request);

            var sourceError = CheckSourceDirectory(sourcePath);
            if (sourceError is not null)
            {
                return Result<BackupResult>.Failure(sourceError.Value);
            }

            if (
                request.Operation is BackupOperation.Update
                && !fileOperationsService.DirectoryExists(destinationPath)
            )
            {
                return Result<BackupResult>.Failure(MessageCode.BackupDestinationMustExist);
            }

            if (
                request.Operation is BackupOperation.Create
                && fileOperationsService.DirectoryExists(destinationPath)
            )
            {
                await fileOperationsService.CleanDirectoryAsync(destinationPath, cancellationToken);
            }

            await fileOperationsService.CreateDirectoryAsync(destinationPath, cancellationToken);

            return await RunBackupAsync(
                sourcePath,
                destinationPath,
                request,
                progress,
                cancellationToken
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Result<BackupResult>.Failure(MessageCode.UnexpectedErrorFormat, ex.Message);
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
    /// Runs the read-only verify path, which requires a password and an existing source directory but
    /// never cleans, creates, or writes to a destination.
    /// </summary>
    /// <param name="request">The backup request identifying the archive to verify.</param>
    /// <param name="progress">A sink that receives incremental status updates.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A successful result whose value reports the verification outcome, carrying a non-success
    /// <see cref="BackupResult"/> when the password is missing or the source path cannot be
    /// normalized, or a failure result when the source directory is absent or an unexpected error
    /// occurs.
    /// </returns>
    private async Task<Result<BackupResult>> ExecuteVerifyAsync(
        BackupRequest request,
        IProgress<BackupStatus> progress,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(request.Password))
        {
            return Result<BackupResult>.Success(
                new BackupResult(
                    false,
                    TimeSpan.Zero,
                    0,
                    0,
                    0,
                    errors: [new LocalizableMessage(MessageCode.PasswordRequired)]
                )
            );
        }

        try
        {
            var sourcePath =
                PathNormalizationHelper.TryNormalize(request.SourcePath, out var normalizeError)
                ?? request.SourcePath;

            if (normalizeError is not null)
            {
                return Result<BackupResult>.Success(
                    new BackupResult(false, TimeSpan.Zero, 0, 0, 0, errors: [normalizeError])
                );
            }

            var sourceError = CheckSourceDirectory(sourcePath);

            return sourceError is not null
                ? Result<BackupResult>.Failure(sourceError.Value)
                : await chunkedBackupService.VerifyAsync(
                    sourcePath,
                    request,
                    progress,
                    cancellationToken
                );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Result<BackupResult>.Failure(MessageCode.UnexpectedErrorFormat, ex.Message);
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
    /// Runs the request validator and turns blocking errors, or warnings the user has not agreed to
    /// proceed past, into a non-success <see cref="BackupResult"/>.
    /// </summary>
    /// <param name="request">The backup request to validate.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A result carrying the validation errors or warnings that stop the operation, or
    /// <see langword="null"/> when the request may proceed.
    /// </returns>
    private async Task<Result<BackupResult>?> ValidateRequestAsync(
        BackupRequest request,
        CancellationToken cancellationToken
    )
    {
        var errors = await backupRequestValidator.AnalyzeErrorsAsync(request, cancellationToken);
        if (errors.Count > 0)
        {
            return Result<BackupResult>.Success(
                new BackupResult(false, TimeSpan.Zero, 0, 0, 0, errors: errors)
            );
        }

        var warnings = await backupRequestValidator.AnalyzeWarningsAsync(
            request,
            cancellationToken
        );
        return warnings.Count > 0 && !request.ProceedOnWarnings
            ? Result<BackupResult>.Success(
                new BackupResult(false, TimeSpan.Zero, 0, 0, 0, warnings: warnings)
            )
            : null;
    }
}
