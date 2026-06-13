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
/// Orchestrates a backup, update, or restore: it validates the request, normalizes paths,
/// prepares the destination directory, and dispatches to the file or directory backup service.
/// </summary>
/// <param name="backupRequestValidator">Validator producing blocking errors and advisory warnings.</param>
/// <param name="fileOperationsService">Service used to inspect and prepare the file system.</param>
/// <param name="fileBackupService">Service that handles single-file backups.</param>
/// <param name="directoryBackupService">Service that handles directory backups.</param>
internal sealed class BackupOrchestrator(
    IBackupRequestValidator backupRequestValidator,
    IFileOperationsService fileOperationsService,
    IFileBackupService fileBackupService,
    IDirectoryBackupService directoryBackupService
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
        var validationResult = await ValidateRequestAsync(request, cancellationToken);
        if (validationResult is not null)
        {
            return validationResult;
        }

        var (sourcePath, destinationPath) = NormalizePaths(request);

        var isDirectory = fileOperationsService.DirectoryExists(sourcePath);
        var isFile = fileOperationsService.FileExists(sourcePath);

        if (!isDirectory && !isFile)
        {
            return Result<BackupResult>.Failure(MessageCode.SourcePathNotExist);
        }

        if (request.Operation == BackupOperation.Update)
        {
            if (!isDirectory)
            {
                return Result<BackupResult>.Failure(MessageCode.UpdateSourceMustBeDirectory);
            }

            if (!fileOperationsService.DirectoryExists(destinationPath))
            {
                return Result<BackupResult>.Failure(MessageCode.BackupDestinationMustExist);
            }
        }

        if (
            request.Operation == BackupOperation.Create
            && isDirectory
            && fileOperationsService.DirectoryExists(destinationPath)
        )
        {
            await CleanDestinationDirectoryAsync(destinationPath, cancellationToken);
        }

        await EnsureDestinationDirectoryAsync(sourcePath, destinationPath, cancellationToken);

        try
        {
            if (request.Operation == BackupOperation.Update)
            {
                return await directoryBackupService.ProcessAsync(
                    sourcePath,
                    destinationPath,
                    request,
                    progress,
                    cancellationToken
                );
            }

            if (isFile)
            {
                return await fileBackupService.ProcessAsync(
                    sourcePath,
                    destinationPath,
                    request,
                    progress,
                    cancellationToken
                );
            }

            return await directoryBackupService.ProcessAsync(
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

    private static (string SourcePath, string DestinationPath) NormalizePaths(BackupRequest request)
    {
        var sourcePath =
            PathNormalizationHelper.TryNormalize(request.SourcePath, out _) ?? request.SourcePath;

        var destinationPath =
            PathNormalizationHelper.TryNormalize(request.DestinationPath, out _)
            ?? request.DestinationPath;

        return (sourcePath, destinationPath);
    }

    private async Task CleanDestinationDirectoryAsync(
        string destinationPath,
        CancellationToken cancellationToken
    )
    {
        await fileOperationsService.CleanDirectoryAsync(destinationPath, cancellationToken);
    }

    private async Task EnsureDestinationDirectoryAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken
    )
    {
        if (fileOperationsService.DirectoryExists(sourcePath))
        {
            await fileOperationsService.CreateDirectoryAsync(destinationPath, cancellationToken);
        }
        else
        {
            var destDir = fileOperationsService.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(destDir))
            {
                await fileOperationsService.CreateDirectoryAsync(destDir, cancellationToken);
            }
        }
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
        if (warnings.Count > 0 && !request.ProceedOnWarnings)
        {
            return Result<BackupResult>.Success(
                new BackupResult(false, TimeSpan.Zero, 0, 0, 0, warnings: warnings)
            );
        }

        return null;
    }
}
