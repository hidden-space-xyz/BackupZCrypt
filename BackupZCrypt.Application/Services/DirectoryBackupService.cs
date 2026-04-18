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
using System.Collections.Concurrent;
using System.Diagnostics;

internal sealed class DirectoryBackupService(
    IEncryptionServiceFactory encryptionServiceFactory,
    ICompressionServiceFactory compressionServiceFactory,
    INameObfuscationServiceFactory nameObfuscationServiceFactory,
    IFileOperationsService fileOperations,
    IManifestService manifestService,
    IEnumerable<IEncryptionAlgorithmStrategy> encryptionStrategies) : IDirectoryBackupService
{
    public async Task<Result<BackupResult>> ProcessAsync(
        string sourcePath,
        string destinationPath,
        BackupRequest request,
        IProgress<BackupStatus> progress,
        CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        if (request.Operation == EncryptOperation.Update)
        {
            return await ProcessUpdateAsync(
                sourcePath, destinationPath, request, progress, stopwatch, cancellationToken);
        }

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
                    return Result<BackupResult>.Failure(
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
                return Result<BackupResult>.Failure(
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
            return Result<BackupResult>.Success(
                new BackupResult(
                    false,
                    stopwatch.Elapsed,
                    0,
                    0,
                    0,
                    errors: [Messages.NoFilesInSourceDirectory]));
        }

        string manifestEncryptedAbsolute = Path.Combine(
            sourcePath,
            BackupConstants.ManifestFileName);
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

        List<(string SourceFilePath, string DestinationFilePath, string OriginalRelativePath, long FileSize)>
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

                    filesWithDestination.Add((file, destinationFilePath, relativePath, fileOperations.GetFileSize(file)));
                }
            }
            else
            {
                foreach (string file in filesToProcess)
                {
                    string relativePath = fileOperations.GetRelativePath(sourcePath, file);
                    string destinationRelativePath =
                        !request.UseEncryption && request.Compression == CompressionMode.None
                            ? relativePath
                            : relativePath + BackupConstants.AppFileExtension;
                    string destinationFilePath = fileOperations.CombinePath(
                        destinationPath,
                        destinationRelativePath);

                    filesWithDestination.Add((file, destinationFilePath, relativePath, fileOperations.GetFileSize(file)));
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

                filesWithDestination.Add((file, destinationFilePath, relativePath, fileOperations.GetFileSize(file)));
            }
        }

        int totalFilesToProcess = filesWithDestination.Count;
        long totalBytes = filesWithDestination.Sum(item => item.FileSize);
        long processedBytes = 0;
        int processedFiles = 0;

        progress?.Report(
            new BackupStatus(0, totalFilesToProcess, 0, totalBytes, TimeSpan.Zero));

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

                                string sourceHash = await fileOperations.ComputeFileHashAsync(file, token);

                                string destRelativePath = fileOperations.GetRelativePath(
                                    destinationPath,
                                    destinationFilePath);

                                manifestEntries.Add(new ManifestEntry(
                                    destRelativePath,
                                    fileItem.OriginalRelativePath,
                                    Convert.ToBase64String(metadata.Salt),
                                    Convert.ToBase64String(metadata.Nonce),
                                    sourceHash));

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

                                string sourceHash = await fileOperations.ComputeFileHashAsync(file, token);

                                manifestEntries.Add(new ManifestEntry(
                                    destRelativePath,
                                    fileItem.OriginalRelativePath,
                                    string.Empty,
                                    string.Empty,
                                    sourceHash));
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

                    long fileSize = fileItem.FileSize;

                    long currentProcessedBytes = Interlocked.Add(ref processedBytes, fileSize);
                    int currentProcessedFiles = Volatile.Read(ref processedFiles);
                    progress?.Report(
                        new BackupStatus(
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
            return Result<BackupResult>.Failure(fatalError);
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
            ? Result<BackupResult>.Failure(
                string.Format(Messages.AllFilesFailedFormat, string.Join("; ", errorList)))
            : Result<BackupResult>.Success(
                new BackupResult(
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
        BackupRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

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

        await using Stream source = fileOperations.OpenReadStream(sourceFile, bufferSize);
        await using Stream destination = fileOperations.CreateWriteStream(
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

        var magic = new byte[BackupConstants.CompressedFileMagic.Length];
        await source.ReadExactlyAsync(magic, cancellationToken);

        if (!magic.AsSpan().SequenceEqual(BackupConstants.CompressedFileMagic.Span))
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

        var compression = (CompressionMode)compressionByte;
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
                string filenameWithExtension = segment + BackupConstants.AppFileExtension;
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

    private async Task<Result<BackupResult>> ProcessUpdateAsync(
        string sourcePath,
        string destinationPath,
        BackupRequest request,
        IProgress<BackupStatus> progress,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        ManifestData? manifestData = await manifestService.TryReadManifestAsync(
            destinationPath,
            [.. encryptionStrategies],
            request.UseEncryption ? request.Password : string.Empty,
            cancellationToken);

        if (manifestData is null)
        {
            return Result<BackupResult>.Failure(Messages.ManifestRequiredForUpdate);
        }

        request = request with
        {
            EncryptionAlgorithm = manifestData.Header.EncryptionAlgorithm,
            KeyDerivationAlgorithm = manifestData.Header.KeyDerivationAlgorithm,
            NameObfuscation = manifestData.Header.NameObfuscation,
            Compression = manifestData.Header.Compression,
        };

        IEncryptionAlgorithmStrategy? encryptionService = null;
        INameObfuscationStrategy? obfuscationService = null;

        if (request.UseEncryption)
        {
            encryptionService = encryptionServiceFactory.Create(request.EncryptionAlgorithm);
        }

        if (request.NameObfuscation != NameObfuscationMode.None)
        {
            obfuscationService = nameObfuscationServiceFactory.Create(request.NameObfuscation);
        }

        Dictionary<string, (string RelativePath, ManifestFileInfo Info)> existingEntries = new(
            StringComparer.OrdinalIgnoreCase);
        if (manifestData.FileMap is not null)
        {
            foreach (var kvp in manifestData.FileMap)
            {
                existingEntries[kvp.Value.OriginalRelativePath] = (kvp.Key, kvp.Value);
            }
        }

        string[] sourceFiles = await fileOperations.GetFilesAsync(
            sourcePath, "*.*", cancellationToken);

        ConcurrentDictionary<string, string> directoryObfuscationCache = new(
            StringComparer.OrdinalIgnoreCase);
        if (obfuscationService is not null && manifestData.FileMap is not null)
        {
            RebuildDirectoryObfuscationCache(manifestData.FileMap, directoryObfuscationCache);
        }

        ConcurrentBag<ManifestEntry> updatedManifestEntries = [];
        List<(string SourceFilePath, string DestinationFilePath, string OriginalRelativePath, long FileSize)>
            filesToProcess = [];
        HashSet<string> sourceOriginalPaths = new(StringComparer.OrdinalIgnoreCase);

        foreach (string file in sourceFiles)
        {
            string originalRelativePath = fileOperations.GetRelativePath(sourcePath, file);
            sourceOriginalPaths.Add(originalRelativePath);

            if (existingEntries.TryGetValue(originalRelativePath, out var existing))
            {
                string currentHash = await fileOperations.ComputeFileHashAsync(file, cancellationToken);

                if (string.Equals(
                        currentHash, existing.Info.SourceHash, StringComparison.Ordinal))
                {
                    updatedManifestEntries.Add(new ManifestEntry(
                        existing.RelativePath,
                        existing.Info.OriginalRelativePath,
                        Convert.ToBase64String(existing.Info.Salt),
                        Convert.ToBase64String(existing.Info.Nonce),
                        existing.Info.SourceHash));
                }
                else
                {
                    string destFilePath = fileOperations.CombinePath(
                        destinationPath, existing.RelativePath);
                    filesToProcess.Add((file, destFilePath, originalRelativePath, fileOperations.GetFileSize(file)));
                }
            }
            else
            {
                string destFilePath;
                if (request.UseEncryption && obfuscationService is not null)
                {
                    destFilePath = ObfuscateFullPath(
                        sourcePath, file, originalRelativePath, destinationPath,
                        obfuscationService, directoryObfuscationCache);
                }
                else
                {
                    destFilePath = fileOperations.CombinePath(
                        destinationPath,
                        request.Compression == CompressionMode.None
                            ? originalRelativePath
                            : originalRelativePath + BackupConstants.AppFileExtension);
                }

                filesToProcess.Add((file, destFilePath, originalRelativePath, fileOperations.GetFileSize(file)));
            }
        }

        foreach (var kvp in existingEntries)
        {
            if (!sourceOriginalPaths.Contains(kvp.Key))
            {
                string destFilePath = fileOperations.CombinePath(
                    destinationPath, kvp.Value.RelativePath);
                try
                {
                    if (fileOperations.FileExists(destFilePath))
                    {
                        File.Delete(destFilePath);
                    }
                }
                catch
                {
                    // Ignore deletion errors for removed files
                }
            }
        }

        int totalFilesToProcess = filesToProcess.Count;
        long totalBytes = filesToProcess.Sum(item => item.FileSize);
        long processedBytes = 0;
        int processedFiles = 0;

        progress?.Report(
            new BackupStatus(0, totalFilesToProcess, 0, totalBytes, TimeSpan.Zero));

        ConcurrentBag<string> errors = [];
        string? fatalError = null;

        if (totalFilesToProcess > 0)
        {
            using CancellationTokenSource linkedCts =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

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
                                EncryptionMetadata metadata =
                                    await encryptionService!.EncryptFileAsync(
                                        file,
                                        destinationFilePath,
                                        request.Password,
                                        request.KeyDerivationAlgorithm,
                                        request.Compression,
                                        token);

                                string sourceHash = await fileOperations.ComputeFileHashAsync(file, token);

                                string destRelativePath = fileOperations.GetRelativePath(
                                    destinationPath,
                                    destinationFilePath);

                                updatedManifestEntries.Add(new ManifestEntry(
                                    destRelativePath,
                                    fileItem.OriginalRelativePath,
                                    Convert.ToBase64String(metadata.Salt),
                                    Convert.ToBase64String(metadata.Nonce),
                                    sourceHash));
                            }
                            else
                            {
                                string destRelativePath = fileOperations.GetRelativePath(
                                    destinationPath,
                                    destinationFilePath);

                                string sourceHash = await fileOperations.ComputeFileHashAsync(file, token);

                                updatedManifestEntries.Add(new ManifestEntry(
                                    destRelativePath,
                                    fileItem.OriginalRelativePath,
                                    string.Empty,
                                    string.Empty,
                                    sourceHash));

                                await CompressFileAsync(
                                    file,
                                    destinationFilePath,
                                    request.Compression,
                                    token);
                            }

                            Interlocked.Increment(ref processedFiles);
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
                                string.Format(
                                    Messages.InsufficientSpaceStoppedFormat, ex.Message),
                                null);
                            await linkedCts.CancelAsync();
                            return;
                        }
                        catch (Domain.Exceptions.EncryptionInvalidPasswordException ex)
                        {
                            Interlocked.CompareExchange(
                                ref fatalError,
                                string.Format(
                                    Messages.InvalidPasswordStoppedFormat, ex.Message),
                                null);
                            await linkedCts.CancelAsync();
                            return;
                        }
                        catch (Domain.Exceptions.EncryptionKeyDerivationException ex)
                        {
                            Interlocked.CompareExchange(
                                ref fatalError,
                                string.Format(
                                    Messages.KeyDerivationStoppedFormat, ex.Message),
                                null);
                            await linkedCts.CancelAsync();
                            return;
                        }
                        catch (Domain.Exceptions.EncryptionFileNotFoundException ex)
                        {
                            errors.Add(
                                string.Format(
                                    Messages.FileNotFoundSkippedFormat, file, ex.Message));
                        }
                        catch (Domain.Exceptions.EncryptionCorruptedFileException ex)
                        {
                            errors.Add(
                                string.Format(
                                    Messages.CorruptedFileSkippedFormat, file, ex.Message));
                        }
                        catch (Domain.Exceptions.EncryptionCipherException ex)
                        {
                            errors.Add(
                                string.Format(Messages.CipherErrorFormat, file, ex.Message));
                        }
                        catch (Domain.Exceptions.EncryptionException ex)
                        {
                            errors.Add(
                                string.Format(Messages.EncryptionErrorFormat, file, ex.Message));
                        }
                        catch (Exception ex) when (!request.UseEncryption)
                        {
                            errors.Add(
                                string.Format(
                                    Messages.CompressionErrorFormat, file, ex.Message));
                        }

                        long fileSize = fileItem.FileSize;

                        long currentProcessedBytes =
                            Interlocked.Add(ref processedBytes, fileSize);
                        int currentProcessedFiles = Volatile.Read(ref processedFiles);
                        progress?.Report(
                            new BackupStatus(
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
                return Result<BackupResult>.Failure(fatalError);
            }
        }

        List<string> errorList = [.. errors];

        ManifestHeader header = new(
            request.EncryptionAlgorithm,
            request.KeyDerivationAlgorithm,
            request.NameObfuscation,
            request.Compression);

        IReadOnlyList<string> manifestErrors;
        if (request.UseEncryption)
        {
            manifestErrors = await manifestService.TrySaveManifestAsync(
                [.. updatedManifestEntries],
                header,
                destinationPath,
                encryptionService!,
                request,
                cancellationToken);
        }
        else
        {
            manifestErrors = await manifestService.TrySavePlainManifestAsync(
                [.. updatedManifestEntries],
                header,
                destinationPath,
                cancellationToken);
        }

        if (manifestErrors.Count > 0)
        {
            errorList.AddRange(manifestErrors);
        }

        stopwatch.Stop();
        bool isSuccess = errorList.Count == 0 && processedFiles == totalFilesToProcess;

        return errorList.Count > 0 && processedFiles == 0 && updatedManifestEntries.IsEmpty
            ? Result<BackupResult>.Failure(
                string.Format(Messages.AllFilesFailedFormat, string.Join("; ", errorList)))
            : Result<BackupResult>.Success(
                new BackupResult(
                    isSuccess,
                    stopwatch.Elapsed,
                    totalBytes,
                    processedFiles,
                    totalFilesToProcess,
                    errors: errorList));
    }

    private static void RebuildDirectoryObfuscationCache(
        Dictionary<string, ManifestFileInfo> fileMap,
        ConcurrentDictionary<string, string> cache)
    {
        foreach (var kvp in fileMap)
        {
            string relPath = kvp.Key;
            string origPath = kvp.Value.OriginalRelativePath;

            string[] origSegments = origPath.Split(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string[] relSegments = relPath.Split(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            for (int i = 0; i < origSegments.Length - 1 && i < relSegments.Length - 1; i++)
            {
                string origDirKey = string.Join(
                    Path.DirectorySeparatorChar.ToString(),
                    origSegments.Take(i + 1));
                cache.TryAdd(origDirKey, relSegments[i]);
            }
        }
    }
}
