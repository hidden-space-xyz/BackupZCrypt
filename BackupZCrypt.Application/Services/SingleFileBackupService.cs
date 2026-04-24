namespace BackupZCrypt.Application.Services;

using BackupZCrypt.Application.Resources;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Application.ValueObjects.Manifest;
using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Factories.Interfaces;
using BackupZCrypt.Domain.Services.Interfaces;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Backup;
using BackupZCrypt.Domain.ValueObjects.Encryption;
using System.Diagnostics;

internal sealed class SingleFileBackupService(
    IEncryptionServiceFactory encryptionServiceFactory,
    ICompressionServiceFactory compressionServiceFactory,
    INameObfuscationServiceFactory nameObfuscationServiceFactory,
    IFileOperationsService fileOperations,
    IManifestService manifestService,
    IEnumerable<IEncryptionAlgorithmStrategy> encryptionStrategies) : ISingleFileBackupService
{
    public async Task<Result<BackupResult>> ProcessAsync(
        string sourcePath,
        string destinationPath,
        BackupRequest request,
        IProgress<BackupStatus> progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (
                request.Operation == EncryptOperation.Decrypt
                && string.Equals(
                    Path.GetFileName(sourcePath),
                    BackupConstants.ManifestFileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                stopwatch.Stop();
                return Result<BackupResult>.Success(
                    new BackupResult(
                        true,
                        stopwatch.Elapsed,
                        0,
                        0,
                        0,
                        errors: [Messages.ManifestIgnored]));
            }

            var destFile = destinationPath;

            if (request.UseEncryption && request.Operation == EncryptOperation.Encrypt
                && request.NameObfuscation != NameObfuscationMode.None)
            {
                var obfuscationService = nameObfuscationServiceFactory.Create(
                    request.NameObfuscation);
                destFile = this.ApplyObfuscationToDestination(
                    destFile,
                    sourcePath,
                    obfuscationService);
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

            progress?.Report(new BackupStatus(0, 1, 0, fileSize, TimeSpan.Zero));

            bool result;
            if (request.UseEncryption)
            {
                var encryptionService =
                    encryptionServiceFactory.Create(request.EncryptionAlgorithm);

                if (request.Operation == EncryptOperation.Encrypt)
                {
                    var metadata = await encryptionService.EncryptFileAsync(
                        sourcePath,
                        destFile,
                        request.Password,
                        request.KeyDerivationAlgorithm,
                        request.Compression,
                        cancellationToken);

                    var destDir = fileOperations.GetDirectoryName(destFile);

                    if (!string.IsNullOrEmpty(destDir))
                    {
                        var destRelativePath = Path.GetFileName(destFile);
                        var originalRelativePath = Path.GetFileName(sourcePath);

                        var sourceHash = await fileOperations.ComputeFileHashAsync(
                            sourcePath, cancellationToken);

                        ManifestEntry entry = new(
                            destRelativePath,
                            originalRelativePath,
                            Convert.ToBase64String(metadata.Salt),
                            Convert.ToBase64String(metadata.Nonce),
                            sourceHash);

                        ManifestHeader header = new(
                            request.EncryptionAlgorithm,
                            request.KeyDerivationAlgorithm,
                            request.NameObfuscation,
                            request.Compression);

                        await manifestService.TrySaveManifestAsync(
                            [entry],
                            header,
                            destDir,
                            encryptionService,
                            request,
                            cancellationToken);
                    }

                    result = true;
                }
                else
                {
                    result = await DecryptWithManifestAsync(
                        encryptionService,
                        sourcePath,
                        destFile,
                        request,
                        cancellationToken);
                }
            }
            else
            {
                if (request.Operation == EncryptOperation.Encrypt)
                {
                    result = await ProcessCompressedFileAsync(
                        sourcePath,
                        destFile,
                        request,
                        cancellationToken);

                    if (result)
                    {
                        var destDir = fileOperations.GetDirectoryName(destFile);

                        if (!string.IsNullOrEmpty(destDir))
                        {
                            var destRelativePath = Path.GetFileName(destFile);
                            var originalRelativePath = Path.GetFileName(sourcePath);

                            ManifestEntry entry = new(
                                destRelativePath,
                                originalRelativePath,
                                string.Empty,
                                string.Empty,
                                string.Empty);

                            ManifestHeader header = new(
                                default,
                                default,
                                NameObfuscationMode.None,
                                request.Compression);

                            await manifestService.TrySavePlainManifestAsync(
                                [entry],
                                header,
                                destDir,
                                cancellationToken);
                        }
                    }
                }
                else
                {
                    result = await DecryptCompressedWithManifestAsync(
                        sourcePath,
                        destFile,
                        cancellationToken);
                }
            }

            progress?.Report(new BackupStatus(1, 1, fileSize, fileSize, stopwatch.Elapsed));
            stopwatch.Stop();

            return Result<BackupResult>.Success(
                new BackupResult(
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
            return Result<BackupResult>.Failure(ex.Message);
        }
        catch (Exception ex) when (!request.UseEncryption && ex is not OperationCanceledException)
        {
            stopwatch.Stop();
            return Result<BackupResult>.Failure(
                string.Format(Messages.CompressionErrorFormat, sourcePath, ex.Message));
        }
    }

    private async Task<bool> DecryptWithManifestAsync(
        IEncryptionAlgorithmStrategy encryptionService,
        string sourcePath,
        string destinationPath,
        BackupRequest request,
        CancellationToken cancellationToken)
    {
        var sourceDir = Path.GetDirectoryName(sourcePath);
        if (string.IsNullOrEmpty(sourceDir))
        {
            throw new InvalidOperationException(
                Messages.ManifestRequiredForDecryption);
        }

        var manifest = await manifestService.TryReadManifestAsync(
            sourceDir,
            [.. encryptionStrategies],
            request.Password,
            cancellationToken) ?? throw new InvalidOperationException(
                Messages.ManifestRequiredForDecryption);

        var sourceFileName = Path.GetFileName(sourcePath);
        if (manifest.FileMap.TryGetValue(sourceFileName, out var fileInfo))
        {
            EncryptionMetadata metadata = new(
                fileInfo.Salt,
                fileInfo.Nonce,
                manifest.Header.Compression);

            var resolvedDestination = Path.Combine(
                Path.GetDirectoryName(destinationPath) ?? destinationPath,
                fileInfo.OriginalRelativePath);

            return await encryptionService.DecryptFileAsync(
                sourcePath,
                resolvedDestination,
                request.Password,
                manifest.Header.KeyDerivationAlgorithm,
                metadata,
                cancellationToken);
        }

        return false;
    }

    private async Task<bool> DecryptCompressedWithManifestAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var sourceDir = Path.GetDirectoryName(sourcePath);
        if (string.IsNullOrEmpty(sourceDir))
        {
            throw new InvalidOperationException(
                Messages.ManifestRequiredForDecryption);
        }

        var manifest = await manifestService.TryReadManifestAsync(
            sourceDir,
            [.. encryptionStrategies],
            string.Empty,
            cancellationToken) ?? throw new InvalidOperationException(
                Messages.ManifestRequiredForDecryption);
        var sourceFileName = Path.GetFileName(sourcePath);
        var resolvedDestination = destinationPath;

        if (manifest.FileMap.TryGetValue(sourceFileName, out var fileInfo))
        {
            resolvedDestination = Path.Combine(
                Path.GetDirectoryName(destinationPath) ?? destinationPath,
                fileInfo.OriginalRelativePath);
        }

        if (manifest.Header.Compression == CompressionMode.None)
        {
            return await CopyFileAsync(sourcePath, resolvedDestination, cancellationToken);
        }

        return await DecompressFileAsync(sourcePath, resolvedDestination, cancellationToken);
    }

    private async Task<bool> ProcessCompressedFileAsync(
        string sourceFile,
        string destinationFile,
        BackupRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var destDir = fileOperations.GetDirectoryName(destinationFile);
        if (!string.IsNullOrEmpty(destDir))
        {
            await fileOperations.CreateDirectoryAsync(destDir, cancellationToken);
        }

        if (request.Operation == EncryptOperation.Encrypt)
        {
            if (request.Compression == CompressionMode.None)
            {
                return await CopyFileAsync(sourceFile, destinationFile, cancellationToken);
            }

            return await CompressFileAsync(
                sourceFile,
                destinationFile,
                request.Compression,
                cancellationToken);
        }

        if (request.Compression == CompressionMode.None)
        {
            return await CopyFileAsync(sourceFile, destinationFile, cancellationToken);
        }

        return await DecompressFileAsync(sourceFile, destinationFile, cancellationToken);
    }

    private async Task<bool> CopyFileAsync(
        string sourceFile,
        string destinationFile,
        CancellationToken cancellationToken)
    {
        const int bufferSize = 81920;

        var destDir = fileOperations.GetDirectoryName(destinationFile);
        if (!string.IsNullOrEmpty(destDir))
        {
            await fileOperations.CreateDirectoryAsync(destDir, cancellationToken);
        }

        await using var source = fileOperations.OpenReadStream(sourceFile, bufferSize);
        await using var destination = fileOperations.CreateWriteStream(
            destinationFile,
            bufferSize);

        await source.CopyToAsync(destination, cancellationToken);

        return true;
    }

    private async Task<bool> CompressFileAsync(
        string sourceFile,
        string destinationFile,
        CompressionMode compression,
        CancellationToken cancellationToken)
    {
        if (compression == CompressionMode.None)
        {
            return await CopyFileAsync(sourceFile, destinationFile, cancellationToken);
        }

        var strategy = compressionServiceFactory.Create(compression);

        await using FileStream source = new(
            sourceFile,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        await using var compressedStream = await strategy.CompressAsync(
            source,
            cancellationToken);

        await using FileStream destination = new(
            destinationFile,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        // Write BZC header: magic bytes + compression mode
        await destination.WriteAsync(BackupConstants.CompressedFileMagic, cancellationToken);
        await destination.WriteAsync(new byte[] { (byte)compression }, cancellationToken);

        await compressedStream.CopyToAsync(destination, cancellationToken);

        return true;
    }

    private async Task<bool> DecompressFileAsync(
        string sourceFile,
        string destinationFile,
        CancellationToken cancellationToken)
    {
        await using FileStream source = new(
            sourceFile,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        // Read and validate BZC header
        var magic = new byte[BackupConstants.CompressedFileMagic.Length];
        await source.ReadExactlyAsync(magic, cancellationToken);

        if (!magic.AsSpan().SequenceEqual(BackupConstants.CompressedFileMagic.Span))
        {
            throw new InvalidOperationException(
                string.Format(Messages.InvalidCompressedFileFormat, sourceFile));
        }

        var compressionByte = source.ReadByte();
        if (compressionByte < 0)
        {
            throw new InvalidOperationException(
                string.Format(Messages.InvalidCompressedFileFormat, sourceFile));
        }

        var compression = (CompressionMode)compressionByte;
        var strategy = compressionServiceFactory.Create(compression);

        await using var decompressedStream = await strategy.DecompressAsync(
            source,
            cancellationToken);

        await using FileStream destination = new(
            destinationFile,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        await decompressedStream.CopyToAsync(destination, cancellationToken);

        return true;
    }

    private string ApplyObfuscationToDestination(
        string destinationFile,
        string sourceFile,
        INameObfuscationStrategy obfuscationService)
    {
        var dir = fileOperations.GetDirectoryName(destinationFile)!;

        var name = Path.GetFileName(destinationFile);
        var obfuscated = obfuscationService.ObfuscateFileName(sourceFile, name);

        return fileOperations.CombinePath(dir, obfuscated);
    }
}
