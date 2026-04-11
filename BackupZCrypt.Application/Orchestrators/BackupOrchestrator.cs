namespace BackupZCrypt.Application.Orchestrators;

using BackupZCrypt.Application.Orchestrators.Interfaces;
using BackupZCrypt.Application.Resources;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.Utilities.Helpers;
using BackupZCrypt.Application.Validators.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Services.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Backup;

internal sealed class BackupOrchestrator(
    IBackupRequestValidator fileProcessingRequestValidator,
    IFileOperationsService fileOperations,
    ISingleFileBackupService singleFileProcessor,
    IDirectoryBackupService directoryProcessor) : IBackupOrchestrator
{
    public async Task<Result<BackupResult>> ExecuteAsync(
        BackupRequest request,
        IProgress<BackupStatus> progress,
        CancellationToken cancellationToken = default)
    {
        Result<BackupResult>? validationResult = await ValidateRequestAsync(
            request,
            cancellationToken);
        if (validationResult is not null)
        {
            return validationResult;
        }

        (string sourcePath, string destinationPath) = NormalizePaths(request);

        bool isDirectory = fileOperations.DirectoryExists(sourcePath);
        bool isFile = fileOperations.FileExists(sourcePath);

        if (!isDirectory && !isFile)
        {
            return Result<BackupResult>.Failure(Messages.SourcePathNotExist);
        }

        if (request.Operation == EncryptOperation.Update)
        {
            if (!isDirectory)
            {
                return Result<BackupResult>.Failure(Messages.UpdateSourceMustBeDirectory);
            }

            if (!fileOperations.DirectoryExists(destinationPath))
            {
                return Result<BackupResult>.Failure(Messages.BackupDestinationMustExist);
            }
        }

        if (request.Operation == EncryptOperation.Encrypt && isDirectory
            && fileOperations.DirectoryExists(destinationPath))
        {
            await CleanDestinationDirectoryAsync(destinationPath, cancellationToken);
        }

        await EnsureDestinationDirectoryAsync(sourcePath, destinationPath, cancellationToken);

        try
        {
            if (request.Operation == EncryptOperation.Update)
            {
                return await directoryProcessor.ProcessAsync(
                    sourcePath,
                    destinationPath,
                    request,
                    progress,
                    cancellationToken);
            }

            if (isFile)
            {
                return await singleFileProcessor.ProcessAsync(
                    sourcePath,
                    destinationPath,
                    request,
                    progress,
                    cancellationToken);
            }

            return await directoryProcessor.ProcessAsync(
                sourcePath,
                destinationPath,
                request,
                progress,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Result<BackupResult>.Failure(
                string.Format(Messages.UnexpectedErrorFormat, ex.Message));
        }
    }

    private static (string SourcePath, string DestinationPath) NormalizePaths(
        BackupRequest request)
    {
        string sourcePath =
            PathNormalizationHelper.TryNormalize(request.SourcePath, out _) ?? request.SourcePath;

        string destinationPath =
            PathNormalizationHelper.TryNormalize(request.DestinationPath, out _)
            ?? request.DestinationPath;

        return (sourcePath, destinationPath);
    }

    private async Task CleanDestinationDirectoryAsync(
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await fileOperations.DeleteDirectoryAsync(destinationPath, recursive: true, cancellationToken);
        await fileOperations.CreateDirectoryAsync(destinationPath, cancellationToken);
    }

    private async Task EnsureDestinationDirectoryAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        if (fileOperations.DirectoryExists(sourcePath))
        {
            await fileOperations.CreateDirectoryAsync(destinationPath, cancellationToken);
        }
        else
        {
            string? destDir = fileOperations.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(destDir))
            {
                await fileOperations.CreateDirectoryAsync(destDir, cancellationToken);
            }
        }
    }

    private async Task<Result<BackupResult>?> ValidateRequestAsync(
        BackupRequest request,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> errors = await fileProcessingRequestValidator.AnalyzeErrorsAsync(
            request,
            cancellationToken);
        if (errors.Count > 0)
        {
            return Result<BackupResult>.Success(
                new BackupResult(false, TimeSpan.Zero, 0, 0, 0, errors: errors));
        }

        IReadOnlyList<string> warnings = await fileProcessingRequestValidator.AnalyzeWarningsAsync(
            request,
            cancellationToken);
        if (warnings.Count > 0 && !request.ProceedOnWarnings)
        {
            return Result<BackupResult>.Success(
                new BackupResult(false, TimeSpan.Zero, 0, 0, 0, warnings: warnings));
        }

        return null;
    }
}
