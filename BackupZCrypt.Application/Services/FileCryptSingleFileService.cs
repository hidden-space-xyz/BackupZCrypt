namespace BackupZCrypt.Application.Services;

using System.Diagnostics;
using BackupZCrypt.Application.Resources;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Factories.Interfaces;
using BackupZCrypt.Domain.Services.Interfaces;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Domain.ValueObjects.FileCrypt;

internal sealed class FileCryptSingleFileService(
    IEncryptionServiceFactory encryptionServiceFactory,
    INameObfuscationServiceFactory nameObfuscationServiceFactory,
    IFileOperationsService fileOperations) : IFileCryptSingleFileService
{
    public async Task<Result<FileCryptResult>> ProcessAsync(
        string sourcePath,
        string destinationPath,
        FileCryptRequest request,
        IProgress<FileCryptStatus> progress,
        CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        IEncryptionAlgorithmStrategy encryptionService = encryptionServiceFactory.Create(
            request.EncryptionAlgorithm);
        INameObfuscationStrategy obfuscationService = nameObfuscationServiceFactory.Create(
            request.NameObfuscation);

        try
        {
            if (
                request.Operation == EncryptOperation.Decrypt
                && string.Equals(
                    Path.GetFileName(sourcePath),
                    FileCryptConstants.ManifestFileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                stopwatch.Stop();
                return Result<FileCryptResult>.Success(
                    new FileCryptResult(
                        true,
                        stopwatch.Elapsed,
                        0,
                        0,
                        0,
                        errors: [Messages.ManifestIgnored]));
            }

            string destFile = destinationPath;

            if (request.Operation == EncryptOperation.Encrypt)
            {
                destFile = this.ApplyObfuscationToDestination(destFile, sourcePath, obfuscationService);
            }

            long fileSize = 0;
            try
            {
                fileSize = fileOperations.GetFileSize(sourcePath);
            }
            catch
            {
                // Ignore file size retrieval errors and proceed with a size of 0 for progress reporting
            }

            progress?.Report(new FileCryptStatus(0, 1, 0, fileSize, TimeSpan.Zero));

            bool result = await ProcessSingleFileAsync(
                encryptionService,
                sourcePath,
                destFile,
                request,
                cancellationToken);

            progress?.Report(new FileCryptStatus(1, 1, fileSize, fileSize, stopwatch.Elapsed));
            stopwatch.Stop();

            return Result<FileCryptResult>.Success(
                new FileCryptResult(
                    result,
                    stopwatch.Elapsed,
                    fileSize,
                    result ? 1 : 0,
                    1,
                    errors: []));
        }
        catch (Domain.Exceptions.EncryptionException ex)
        {
            stopwatch.Stop();
            return Result<FileCryptResult>.Failure(ex.Message);
        }
    }

    private static Task<bool> ProcessSingleFileAsync(
        IEncryptionAlgorithmStrategy encryptionService,
        string sourceFile,
        string destinationFile,
        FileCryptRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return request.Operation switch
        {
            EncryptOperation.Encrypt => encryptionService.EncryptFileAsync(
                sourceFile,
                destinationFile,
                request.Password,
                request.KeyDerivationAlgorithm,
                request.Compression,
                cancellationToken),
            EncryptOperation.Decrypt => encryptionService.DecryptFileAsync(
                sourceFile,
                destinationFile,
                request.Password,
                request.KeyDerivationAlgorithm,
                cancellationToken),
            _ => throw new NotSupportedException(
                string.Format(Messages.UnsupportedOperationFormat, request.Operation)),
        };
    }

    private string ApplyObfuscationToDestination(
        string destinationFile,
        string sourceFile,
        INameObfuscationStrategy obfuscationService)
    {
        string dir =
            fileOperations.GetDirectoryName(destinationFile)
            ?? Path.GetDirectoryName(destinationFile)!;

        string name = Path.GetFileName(destinationFile);
        string obfuscated = obfuscationService.ObfuscateFileName(sourceFile, name);

        return fileOperations.CombinePath(dir, obfuscated);
    }
}
