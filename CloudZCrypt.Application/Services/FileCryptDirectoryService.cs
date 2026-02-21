namespace CloudZCrypt.Application.Services;

using System.Collections.Concurrent;
using System.Diagnostics;
using CloudZCrypt.Application.Resources;
using CloudZCrypt.Application.Services.Interfaces;
using CloudZCrypt.Application.ValueObjects;
using CloudZCrypt.Application.ValueObjects.Manifest;
using CloudZCrypt.Domain.Constants;
using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Factories.Interfaces;
using CloudZCrypt.Domain.Services.Interfaces;
using CloudZCrypt.Domain.Strategies.Interfaces;
using CloudZCrypt.Domain.ValueObjects.FileCrypt;

internal sealed class FileCryptDirectoryService(
    IEncryptionServiceFactory encryptionServiceFactory,
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

        IEncryptionAlgorithmStrategy encryptionService;
        INameObfuscationStrategy obfuscationService;

        ConcurrentBag<ManifestEntry> manifestEntries = [];
        Dictionary<string, string>? manifestMap = null;

        if (request.Operation == EncryptOperation.Decrypt)
        {
            ManifestData? manifestData = await manifestService.TryReadManifestAsync(
                sourcePath,
                [.. encryptionStrategies],
                request.Password,
                cancellationToken);

            if (manifestData is not null)
            {
                manifestMap = manifestData.FileMap;
                encryptionService = encryptionServiceFactory.Create(
                    manifestData.Header.EncryptionAlgorithm);
                obfuscationService = nameObfuscationServiceFactory.Create(
                    manifestData.Header.NameObfuscation);
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
                obfuscationService = nameObfuscationServiceFactory.Create(request.NameObfuscation);
            }
        }
        else
        {
            encryptionService = encryptionServiceFactory.Create(request.EncryptionAlgorithm);
            obfuscationService = nameObfuscationServiceFactory.Create(request.NameObfuscation);
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

        long totalBytes = filesToProcess.Sum(fileOperations.GetFileSize);
        long processedBytes = 0;
        int processedFiles = 0;

        ConcurrentDictionary<string, string> directoryObfuscationCache = new(
            StringComparer.OrdinalIgnoreCase);

        progress?.Report(
            new FileCryptStatus(0, filesToProcess.Length, 0, totalBytes, TimeSpan.Zero));

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
                filesToProcess,
                parallelOptions,
                async (file, token) =>
                {
                    string relativePath = fileOperations.GetRelativePath(sourcePath, file);
                    string destinationFilePath;

                    if (request.Operation == EncryptOperation.Encrypt)
                    {
                        destinationFilePath = ObfuscateFullPath(
                            sourcePath,
                            file,
                            relativePath,
                            destinationPath,
                            obfuscationService,
                            directoryObfuscationCache);

                        string obfuscatedRelativePath = fileOperations.GetRelativePath(
                            destinationPath,
                            destinationFilePath);
                        manifestEntries.Add(
                            new ManifestEntry(relativePath, obfuscatedRelativePath));
                    }
                    else
                    {
                        destinationFilePath = fileOperations.CombinePath(
                            destinationPath,
                            relativePath.Replace(FileCryptConstants.AppFileExtension, string.Empty));

                        if (
                            manifestMap is not null
                            && manifestMap.TryGetValue(
                                relativePath,
                                out string? originalRelativePath))
                        {
                            destinationFilePath = fileOperations.CombinePath(
                                destinationPath,
                                originalRelativePath);
                        }
                    }

                    string? destDir = fileOperations.GetDirectoryName(destinationFilePath);
                    if (!string.IsNullOrEmpty(destDir))
                    {
                        await fileOperations.CreateDirectoryAsync(destDir, token);
                    }

                    try
                    {
                        bool operationResult = await ProcessSingleFileAsync(
                            encryptionService,
                            file,
                            destinationFilePath,
                            request,
                            token);
                        if (operationResult)
                        {
                            Interlocked.Increment(ref processedFiles);
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
                            filesToProcess.Length,
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
            IReadOnlyList<string> manifestErrors = await manifestService.TrySaveManifestAsync(
                [.. manifestEntries],
                header,
                destinationPath,
                encryptionService,
                request,
                cancellationToken);
            if (manifestErrors.Count > 0)
            {
                errorList.AddRange(manifestErrors);
            }
        }

        stopwatch.Stop();
        bool isSuccess = errorList.Count == 0 && processedFiles == filesToProcess.Length;

        return errorList.Count > 0 && processedFiles == 0
            ? Result<FileCryptResult>.Failure(
                string.Format(Messages.AllFilesFailedFormat, string.Join("; ", errorList)))
            : Result<FileCryptResult>.Success(
                new FileCryptResult(
                    isSuccess,
                    stopwatch.Elapsed,
                    totalBytes,
                    processedFiles,
                    filesToProcess.Length,
                    errors: errorList));
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
            _ => throw new NotSupportedException($"Unsupported operation: {request.Operation}"),
        };
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
