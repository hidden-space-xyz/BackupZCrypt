namespace BackupZCrypt.Application.Orchestrators;

using BackupZCrypt.Application.Orchestrators.Interfaces;
using BackupZCrypt.Application.Resources;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.Utilities.Helpers;
using BackupZCrypt.Application.Validators.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Domain.Services.Interfaces;
using BackupZCrypt.Domain.ValueObjects.FileCrypt;

internal sealed class FileCryptOrchestrator(
    IFileCryptRequestValidator fileProcessingRequestValidator,
    IFileOperationsService fileOperations,
    IFileCryptSingleFileService singleFileProcessor,
    IFileCryptDirectoryService directoryProcessor) : IFileCryptOrchestrator
{
    public async Task<Result<FileCryptResult>> ExecuteAsync(
        FileCryptRequest request,
        IProgress<FileCryptStatus> progress,
        CancellationToken cancellationToken = default)
    {
        Result<FileCryptResult>? validationResult = await ValidateRequestAsync(
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
            return Result<FileCryptResult>.Failure(Messages.SourcePathNotExist);
        }

        await EnsureDestinationDirectoryAsync(sourcePath, destinationPath, cancellationToken);

        try
        {
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
            return Result<FileCryptResult>.Failure(
                string.Format(Messages.UnexpectedErrorFormat, ex.Message));
        }
    }

    private static (string SourcePath, string DestinationPath) NormalizePaths(
        FileCryptRequest request)
    {
        string sourcePath =
            PathNormalizationHelper.TryNormalize(request.SourcePath, out _) ?? request.SourcePath;

        string destinationPath =
            PathNormalizationHelper.TryNormalize(request.DestinationPath, out _)
            ?? request.DestinationPath;

        return (sourcePath, destinationPath);
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

    private async Task<Result<FileCryptResult>?> ValidateRequestAsync(
        FileCryptRequest request,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> errors = await fileProcessingRequestValidator.AnalyzeErrorsAsync(
            request,
            cancellationToken);
        if (errors.Count > 0)
        {
            return Result<FileCryptResult>.Success(
                new FileCryptResult(false, TimeSpan.Zero, 0, 0, 0, errors: errors));
        }

        IReadOnlyList<string> warnings = await fileProcessingRequestValidator.AnalyzeWarningsAsync(
            request,
            cancellationToken);
        if (warnings.Count > 0 && !request.ProceedOnWarnings)
        {
            return Result<FileCryptResult>.Success(
                new FileCryptResult(false, TimeSpan.Zero, 0, 0, 0, warnings: warnings));
        }

        return null;
    }
}
