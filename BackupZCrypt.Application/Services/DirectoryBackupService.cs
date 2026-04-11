namespace BackupZCrypt.Application.Services;

using System.Collections.Concurrent;
using System.Diagnostics;
using BackupZCrypt.Application.Resources;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Application.ValueObjects.Manifest;
using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Factories.Interfaces;
using BackupZCrypt.Domain.Services.Interfaces;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Encryption;
using BackupZCrypt.Domain.ValueObjects.FileCrypt;

internal sealed class DirectoryBackupService(
    IEncryptionServiceFactory encryptionServiceFactory,
    ICompressionServiceFactory compressionServiceFactory,
    INameObfuscationServiceFactory nameObfuscationServiceFactory,
    IFileOperationsService fileOperations,
    IManifestService manifestService,
    IEnumerable<IEncryptionAlgorithmStrategy> encryptionStrategies) : IFileCryptDirectoryService
{
    public async Task<Result<FileCryptResult>> ProcessAsync(
        string sourcePath,
        string destinationPath,
        FileCryptRequest request,
        IProgress<FileCryptStatus> progress,
        CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        IEncryptionAlgorithmStrategy? encryptionService = null;
        INameObfuscationStrategy? obfuscationService = null;

        ConcurrentBag<ManifestEntry> manifestEntries = [];
        Dictionary<string, ManifestFileInfo>? manifestMap = null;

        if (request.UseEncryption)
        {
            if (request.Operation == EncryptOperation.Decrypt)
            {
                ManifestData? manifestData = await manifestService.TryReadManifestAsync(
                    sourcePath,
                    [.. encryptionStrategies],
                    request.Password,
                    cancellationToken);

                if (manifestData is null)
                {
                    return Result<FileCryptResult>.Failure(
                        Messages.ManifestRequiredForDecryption);
                }

                manifestMap = manifestData.FileMap;
                encryptionService = encryptionServiceFactory.Create(
                    manifestData.Header.EncryptionAlgorithm);

                if (manifestData.Header.NameObfuscation != NameObfuscationMode.None)
                {
                    obfuscationService = nameObfuscationServiceFactory.Create(
                        manifestData.Header.NameObfuscation);
                }

                request = request with
                {
                    EncryptionAlgorithm = manifestData.Header.EncryptionAlgorithm,
                    KeyDerivationAlgorithm = manifestData.Header.KeyDerivationAlgorithm,
                    NameObfuscation = manifestData.Header.NameObfuscation,
                    Compression = manifestData.Header.Compression,
                };
            }
            else
            {
                encryptionService = encryptionServiceFactory.Create(request.EncryptionAlgorithm);

                if (request.NameObfuscation != NameObfuscationMode.None)
                {
                    obfuscationService = nameObfuscationServiceFactory.Create(request.NameObfuscation);
                }
            }
        }
        else if (request.Operation == EncryptOperation.Decrypt)
        {
            ManifestData? manifestData = await manifestService.TryReadManifestAsync(
                sourcePath,
                [.. encryptionStrategies],
                string.Empty,
                cancellationToken);

            if (manifestData is null)
            {
                return Result<FileCryptResult>.Failure(
                    Messages.ManifestRequiredForDecryption);
            }

            manifestMap = manifestData.FileMap;
            request = request with
            {
                Compression = manifestData.Header.Compression,
            };
        }

        string[] files = await fileOperations.GetFilesAsync(sourcePath, "*.*", cancellationToken);

        if (files.Length == 0)
        {
            stopwatch.Stop();
            return Result<FileCryptResult>.Success(
                new FileCryptResult(
                    false,
                    stopwatch.Elapsed,
                    0,
                    0,
                    0,
                    errors: [Messages.NoFilesInSourceDirectory]));
        }

        string manifestEncryptedAbsolute = Path.Combine(
            sourcePath,
            FileCryptConstants.ManifestFileName);
        string manifestEncryptedRelative = fileOperations.GetRelativePath(
            sourcePath,
            manifestEncryptedAbsolute);

        string[] filesToProcess = files;
        if (request.Operation == EncryptOperation.Decrypt)
        {
            filesToProcess = files
                .Where(f =>
                    !string.Equals(
                        fileOperations.GetRelativePath(sourcePath, f),
                        manifestEncryptedRelative,
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        if (request.Operation == EncryptOperation.Encrypt)
        {
            await fileOperations.CreateDirectoryAsync(destinationPath, cancellationToken);
        }

        ConcurrentDictionary<string, string> directoryObfuscationCache = new(
            StringComparer.OrdinalIgnoreCase);

        List<(string SourceFilePath, string DestinationFilePath, string OriginalRelativePath)>
            filesWithDestination = [];

        if (request.Operation == EncryptOperation.Encrypt)
        {
            if (request.UseEncryption && obfuscationService is not null)
            {
                HashSet<string> uniqueDestinationPaths = new(StringComparer.OrdinalIgnoreCase);

                foreach (string file in filesToProcess)
                {
                    string relativePath = fileOperations.GetRelativePath(sourcePath, file);
                    string destinationFilePath = ObfuscateFullPath(
                        sourcePath,
                        file,
                        relativePath,
                        destinationPath,
                        obfuscationService,
                        directoryObfuscationCache);

                    if (!uniqueDestinationPaths.Add(destinationFilePath))
                    {
                        continue;
                    }

                    filesWithDestination.Add((file, destinationFilePath, relativePath));
                }
            }
            else
            {
                foreach (string file in filesToProcess)
                {
                    string relativePath = fileOperations.GetRelativePath(sourcePath, file);
                    string destinationFilePath = fileOperations.CombinePath(
                        destinationPath,
                        relativePath + FileCryptConstants.AppFileExtension);

                    filesWithDestination.Add((file, destinationFilePath, relativePath));
                }
            }
        }
        else
        {
            foreach (string file in filesToProcess)
            {
                string relativePath = fileOperations.GetRelativePath(sourcePath, file);
                string destinationFilePath;

                if (
                    manifestMap is not null
                    && manifestMap.TryGetValue(relativePath, out ManifestFileInfo? fileInfo))
                {
                    destinationFilePath = fileOperations.CombinePath(
                        destinationPath,
                        fileInfo.OriginalRelativePath);
                }
                else
                {
                    destinationFilePath = fileOperations.CombinePath(
                        destinationPath,
                        relativePath);
                }

                filesWithDestination.Add((file, destinationFilePath, relativePath));
            }
        }

        int totalFilesToProcess = filesWithDestination.Count;
        long totalBytes = filesWithDestination.Sum(
            item => fileOperations.GetFileSize(item.SourceFilePath));
        long processedBytes = 0;
        int processedFiles = 0;

        progress?.Report(
            new FileCryptStatus(0, totalFilesToProcess, 0, totalBytes, TimeSpan.Zero));

        ConcurrentBag<string> errors = [];
        string? fatalError = null;

        using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);

        int maxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount);
        ParallelOptions parallelOptions = new()
        {
            MaxDegreeOfParallelism = maxDegreeOfParallelism,
            CancellationToken = linkedCts.Token,
        };

        try
        {
            await Parallel.ForEachAsync(
                filesWithDestination,
                parallelOptions,
                async (fileItem, token) =>
                {
                    string file = fileItem.SourceFilePath;
                    string destinationFilePath = fileItem.DestinationFilePath;

                    string? destDir = fileOperations.GetDirectoryName(destinationFilePath);
                    if (!string.IsNullOrEmpty(destDir))
                    {
                        await fileOperations.CreateDirectoryAsync(destDir, token);
                    }

                    try
                    {
                        if (request.UseEncryption)
                        {
                            if (request.Operation == EncryptOperation.Encrypt)
                            {
                                EncryptionMetadata metadata =
                                    await encryptionService!.EncryptFileAsync(
                                        file,
                                        destinationFilePath,
                                        request.Password,
                                        request.KeyDerivationAlgorithm,
                                        request.Compression,
                                        token);

                                string destRelativePath = fileOperations.GetRelativePath(
                                    destinationPath,
                                    destinationFilePath);

                                manifestEntries.Add(new ManifestEntry(
                                    destRelativePath,
                                    fileItem.OriginalRelativePath,
                                    Convert.ToBase64String(metadata.Salt),
                                    Convert.ToBase64String(metadata.Nonce)));

                                Interlocked.Increment(ref processedFiles);
                            }
                            else
                            {
                                string relativePath = fileItem.OriginalRelativePath;

                                if (
                                    manifestMap is not null
                                    && manifestMap.TryGetValue(
                                        relativePath,
                                        out ManifestFileInfo? fileInfo))
                                {
                                    EncryptionMetadata metadata = new(
                                        fileInfo.Salt,
                                        fileInfo.Nonce,
                                        request.Compression);

                                    bool ok = await encryptionService!.DecryptFileAsync(
                                        file,
                                        destinationFilePath,
                                        request.Password,
                                        request.KeyDerivationAlgorithm,
                                        metadata,
                                        token);

                                    if (ok)
                                    {
                                        Interlocked.Increment(ref processedFiles);
                                    }
                                }
                            }
                        }
                        else
                        {
                            if (request.Operation == EncryptOperation.Encrypt)
                            {
                                string destRelativePath = fileOperations.GetRelativePath(
                                    destinationPath,
                                    destinationFilePath);

                                manifestEntries.Add(new ManifestEntry(
                                    destRelativePath,
                                    fileItem.OriginalRelativePath,
                                    string.Empty,
                                    string.Empty));
                            }

                            bool operationResult = await ProcessCompressedFileAsync(
                                file,
                                destinationFilePath,
                                request,
                                token);

                            if (operationResult)
                            {
                                Interlocked.Increment(ref processedFiles);
                            }
                        }
                    }
                    catch (Domain.Exceptions.EncryptionAccessDeniedException ex)
                    {
                        Interlocked.CompareExchange(
                            ref fatalError,
                            string.Format(Messages.AccessDeniedStoppedFormat, ex.Message),
                            null);
                        await linkedCts.CancelAsync();
                        return;
                    }
                    catch (Domain.Exceptions.EncryptionInsufficientSpaceException ex)
                    {
                        Interlocked.CompareExchange(
                            ref fatalError,
                            string.Format(Messages.InsufficientSpaceStoppedFormat, ex.Message),
                            null);
                        await linkedCts.CancelAsync();
                        return;
                    }
                    catch (Domain.Exceptions.EncryptionInvalidPasswordException ex)
                    {
                        Interlocked.CompareExchange(
                            ref fatalError,
                            string.Format(Messages.InvalidPasswordStoppedFormat, ex.Message),
                            null);
                        await linkedCts.CancelAsync();
                        return;
                    }
                    catch (Domain.Exceptions.EncryptionKeyDerivationException ex)
                    {
                        Interlocked.CompareExchange(
                            ref fatalError,
                            string.Format(Messages.KeyDerivationStoppedFormat, ex.Message),
                            null);
                        await linkedCts.CancelAsync();
                        return;
                    }
                    catch (Domain.Exceptions.EncryptionFileNotFoundException ex)
                    {
                        errors.Add(
                            string.Format(Messages.FileNotFoundSkippedFormat, file, ex.Message));
                    }
                    catch (Domain.Exceptions.EncryptionCorruptedFileException ex)
                    {
                        errors.Add(
                            string.Format(Messages.CorruptedFileSkippedFormat, file, ex.Message));
                    }
                    catch (Domain.Exceptions.EncryptionCipherException ex)
                    {
                        errors.Add(string.Format(Messages.CipherErrorFormat, file, ex.Message));
                    }
                    catch (Domain.Exceptions.EncryptionException ex)
                    {
                        errors.Add(string.Format(Messages.EncryptionErrorFormat, file, ex.Message));
                    }
                    catch (Exception ex) when (!request.UseEncryption)
                    {
                        errors.Add(
                            string.Format(Messages.CompressionErrorFormat, file, ex.Message));
                    }

                    long fileSize = 0;
                    try
                    {
                        fileSize = fileOperations.GetFileSize(file);
                    }
                    catch
                    {
                        // Ignore file size retrieval errors and proceed with a size of 0 for progress reporting
                    }

                    long currentProcessedBytes = Interlocked.Add(ref processedBytes, fileSize);
                    int currentProcessedFiles = Volatile.Read(ref processedFiles);
                    progress?.Report(
                        new FileCryptStatus(
                            currentProcessedFiles,
                            totalFilesToProcess,
                            currentProcessedBytes,
                            totalBytes,
                            stopwatch.Elapsed));
                });
        }
        catch (OperationCanceledException) when (fatalError is not null)
        {
            stopwatch.Stop();
            return Result<FileCryptResult>.Failure(fatalError);
        }

        List<string> errorList = [.. errors];

        if (request.Operation == EncryptOperation.Encrypt && !manifestEntries.IsEmpty)
        {
            ManifestHeader header = new(
                request.EncryptionAlgorithm,
                request.KeyDerivationAlgorithm,
                request.NameObfuscation,
                request.Compression);

            IReadOnlyList<string> manifestErrors;
            if (request.UseEncryption)
            {
                manifestErrors = await manifestService.TrySaveManifestAsync(
                    [.. manifestEntries],
                    header,
                    destinationPath,
                    encryptionService!,
                    request,
                    cancellationToken);
            }
            else
            {
                manifestErrors = await manifestService.TrySavePlainManifestAsync(
                    [.. manifestEntries],
                    header,
                    destinationPath,
                    cancellationToken);
            }

            if (manifestErrors.Count > 0)
            {
                errorList.AddRange(manifestErrors);
            }
        }

        stopwatch.Stop();
        bool isSuccess = errorList.Count == 0 && processedFiles == totalFilesToProcess;

        return errorList.Count > 0 && processedFiles == 0
            ? Result<FileCryptResult>.Failure(
                string.Format(Messages.AllFilesFailedFormat, string.Join("; ", errorList)))
            : Result<FileCryptResult>.Success(
                new FileCryptResult(
                    isSuccess,
                    stopwatch.Elapsed,
                    totalBytes,
                    processedFiles,
                    totalFilesToProcess,
                    errors: errorList));
    }

    private async Task<bool> ProcessCompressedFileAsync(
        string sourceFile,
        string destinationFile,
        FileCryptRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Operation == EncryptOperation.Encrypt)
        {
            return await CompressFileAsync(
                sourceFile,
                destinationFile,
                request.Compression,
                cancellationToken);
        }

        return await DecompressFileAsync(sourceFile, destinationFile, cancellationToken);
    }

    private async Task<bool> CompressFileAsync(
        string sourceFile,
        string destinationFile,
        CompressionMode compression,
        CancellationToken cancellationToken)
    {
        ICompressionStrategy strategy = compressionServiceFactory.Create(compression);

        await using FileStream source = new(
            sourceFile,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        await using Stream compressedStream = await strategy.CompressAsync(
            source,
            cancellationToken);

        await using FileStream destination = new(
            destinationFile,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        destination.Write(FileCryptConstants.CompressedFileMagic);
        destination.WriteByte((byte)compression);

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

        byte[] magic = new byte[FileCryptConstants.CompressedFileMagic.Length];
        await source.ReadExactlyAsync(magic, cancellationToken);

        if (!magic.AsSpan().SequenceEqual(FileCryptConstants.CompressedFileMagic))
        {
            throw new InvalidOperationException(
                string.Format(Messages.InvalidCompressedFileFormat, sourceFile));
        }

        int compressionByte = source.ReadByte();
        if (compressionByte < 0)
        {
            throw new InvalidOperationException(
                string.Format(Messages.InvalidCompressedFileFormat, sourceFile));
        }

        CompressionMode compression = (CompressionMode)compressionByte;
        ICompressionStrategy strategy = compressionServiceFactory.Create(compression);

        await using Stream decompressedStream = await strategy.DecompressAsync(
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

    private string ObfuscateFullPath(
        string sourcePath,
        string sourceFilePath,
        string relativePath,
        string destinationRoot,
        INameObfuscationStrategy obfuscationService,
        ConcurrentDictionary<string, string> directoryCache)
    {
        string[] segments = relativePath.Split(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);

        List<string> obfuscatedSegments = new(segments.Length);
        string currentSourcePath = sourcePath;

        for (int i = 0; i < segments.Length; i++)
        {
            string segment = segments[i];
            bool isLastSegment = i == segments.Length - 1;

            if (isLastSegment)
            {
                string filenameWithExtension = segment + FileCryptConstants.AppFileExtension;
                string obfuscatedFilename = obfuscationService.ObfuscateFileName(
                    sourceFilePath,
                    filenameWithExtension);
                obfuscatedSegments.Add(obfuscatedFilename);
            }
            else
            {
                currentSourcePath = Path.Combine(currentSourcePath, segment);
                string directoryKey = fileOperations.GetRelativePath(sourcePath, currentSourcePath);
                string capturedSourcePath = currentSourcePath;
                string capturedSegment = segment;

                string obfuscatedDirName = directoryCache.GetOrAdd(
                    directoryKey,
                    _ => obfuscationService.ObfuscateFileName(capturedSourcePath, capturedSegment));

                obfuscatedSegments.Add(obfuscatedDirName);
            }
        }

        return fileOperations.CombinePath([destinationRoot, .. obfuscatedSegments]);
    }
}
