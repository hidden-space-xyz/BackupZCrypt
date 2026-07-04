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
/// is read-only and takes a dedicated path that skips destination preparation.
/// </summary>
/// <param name="backupRequestValidator">Validator producing blocking errors and advisory warnings.</param>
/// <param name="fileOperationsService">Service used to inspect and prepare the file system.</param>
/// <param name="chunkedBackupService">Service that performs the chunk-based backup, update, restore, and verify operations.</param>
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
        if (request.Operation == BackupOperation.Verify)
        {
            return await ExecuteVerifyAsync(request, progress, cancellationToken);
        }

        var validationResult = await ValidateRequestAsync(request, cancellationToken);
        if (validationResult is not null)
        {
            return validationResult;
        }

        var (sourcePath, destinationPath) = NormalizePaths(request);

        if (!fileOperationsService.DirectoryExists(sourcePath))
        {
            return Result<BackupResult>.Failure(
                fileOperationsService.FileExists(sourcePath)
                    ? MessageCode.SourceMustBeDirectory
                    : MessageCode.SourcePathNotExist
            );
        }

        if (
            request.Operation == BackupOperation.Update
            && !fileOperationsService.DirectoryExists(destinationPath)
        )
        {
            return Result<BackupResult>.Failure(MessageCode.BackupDestinationMustExist);
        }

        if (
            request.Operation == BackupOperation.Create
            && fileOperationsService.DirectoryExists(destinationPath)
        )
        {
            await fileOperationsService.CleanDirectoryAsync(destinationPath, cancellationToken);
        }

        await fileOperationsService.CreateDirectoryAsync(destinationPath, cancellationToken);

        try
        {
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
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };
    }

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

        var sourcePath =
            PathNormalizationHelper.TryNormalize(request.SourcePath, out var normalizeError)
            ?? request.SourcePath;

        if (normalizeError is not null)
        {
            return Result<BackupResult>.Success(
                new BackupResult(false, TimeSpan.Zero, 0, 0, 0, errors: [normalizeError])
            );
        }

        if (!fileOperationsService.DirectoryExists(sourcePath))
        {
            return Result<BackupResult>.Failure(
                fileOperationsService.FileExists(sourcePath)
                    ? MessageCode.SourceMustBeDirectory
                    : MessageCode.SourcePathNotExist
            );
        }

        try
        {
            return await chunkedBackupService.VerifyAsync(
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

    private static (string SourcePath, string DestinationPath) NormalizePaths(BackupRequest request)
    {
        var sourcePath =
            PathNormalizationHelper.TryNormalize(request.SourcePath, out _) ?? request.SourcePath;

        var destinationPath =
            PathNormalizationHelper.TryNormalize(request.DestinationPath, out _)
            ?? request.DestinationPath;

        return (sourcePath, destinationPath);
    }

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
