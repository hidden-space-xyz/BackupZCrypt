using BackupZCrypt.Application.Constants;
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

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;

namespace BackupZCrypt.Application.Services;

internal sealed class ChunkedBackupService(
    ICompressionServiceFactory compressionServiceFactory,
    IEnumerable<IEncryptionAlgorithmStrategy> encryptionStrategies,
    IFileOperationsService fileOperationsService,
    IManifestService manifestService,
    IChunkingStrategy chunkingStrategy,
    IKeyDerivationServiceFactory keyDerivationServiceFactory) : IChunkedBackupService
{
    private const int KeySizeBytes = 32;
    private const int NonceSizeBytes = 12;
    private const int SaltSizeBytes = 32;
    private readonly Dictionary<EncryptionAlgorithm, IEncryptionAlgorithmStrategy> encryptionStrategiesById =
        encryptionStrategies.ToDictionary(static strategy => strategy.Id, static strategy => strategy);

    public async Task<Result<BackupResult>> CreateAsync(
        string sourcePath,
        string destinationPath,
        BackupRequest request,
        IProgress<BackupStatus> progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        var isFile = fileOperationsService.FileExists(sourcePath);
        var isDirectory = fileOperationsService.DirectoryExists(sourcePath);

        string[] sourceFiles;
        string sourceRoot;

        if (isFile)
        {
            sourceFiles = [sourcePath];
            sourceRoot = fileOperationsService.GetDirectoryName(sourcePath) ?? sourcePath;
        }
        else if (isDirectory)
        {
            sourceRoot = sourcePath;
            sourceFiles = await fileOperationsService.GetFilesAsync(
                sourcePath, "*.*", cancellationToken);
        }
        else
        {
            return Result<BackupResult>.Failure(Messages.SourcePathNotExist);
        }

        if (sourceFiles.Length == 0)
        {
            stopwatch.Stop();
            return Result<BackupResult>.Success(
                new BackupResult(false, stopwatch.Elapsed, 0, 0, 0,
                    errors: [Messages.NoFilesInSourceDirectory]));
        }

        await fileOperationsService.CreateDirectoryAsync(destinationPath, cancellationToken);
        var chunksDir = fileOperationsService.CombinePath(
            destinationPath, BackupConstants.ChunksDirectoryName);
        await fileOperationsService.CreateDirectoryAsync(chunksDir, cancellationToken);

        var masterSalt = GenerateSalt();
        var masterKey = DeriveKey(request.Password, masterSalt, request.KeyDerivationAlgorithm);
        var encryptionKey = DeriveSubKey(masterKey, "chunk-encryption"u8);
        var namingKey = DeriveSubKey(masterKey, "chunk-naming"u8);

        try
        {
            var encryptionStrategy = ResolveEncryptionStrategy(request.EncryptionAlgorithm);
            ICompressionStrategy? compressionStrategy = null;
            if (request.Compression != CompressionMode.None)
            {
                compressionStrategy = compressionServiceFactory.Create(request.Compression);
            }

            var totalFiles = sourceFiles.Length;
            var totalBytes = sourceFiles.Sum(f =>
            {
                try { return fileOperationsService.GetFileSize(f); }
                catch { return 0L; }
            });

            progress?.Report(new BackupStatus(0, totalFiles, 0, totalBytes, TimeSpan.Zero));

            ConcurrentBag<ChunkManifestFileEntry> fileEntries = [];
            ConcurrentBag<string> errors = [];
            long processedBytes = 0;
            var processedFiles = 0;
            string? fatalError = null;
            ConcurrentDictionary<string, bool> storedChunks = new(StringComparer.Ordinal);

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var maxDop = Math.Max(1, Environment.ProcessorCount);

            try
            {
                await Parallel.ForEachAsync(
                    sourceFiles,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = maxDop,
                        CancellationToken = linkedCts.Token,
                    },
                    async (file, token) =>
                    {
                        try
                        {
                            var relativePath = isFile
                                ? Path.GetFileName(file)
                                : fileOperationsService.GetRelativePath(sourceRoot, file);

                            var fileSize = fileOperationsService.GetFileSize(file);

                            var entry = await ChunkAndEncryptFileAsync(
                                file,
                                relativePath,
                                fileSize,
                                chunksDir,
                                encryptionKey,
                                namingKey,
                                encryptionStrategy,
                                compressionStrategy,
                                storedChunks,
                                token);

                            fileEntries.Add(entry);
                            Interlocked.Increment(ref processedFiles);
                            var currentBytes = Interlocked.Add(ref processedBytes, fileSize);

                            progress?.Report(new BackupStatus(
                                Volatile.Read(ref processedFiles),
                                totalFiles,
                                currentBytes,
                                totalBytes,
                                stopwatch.Elapsed));
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            if (IsFileLevelError(ex))
                            {
                                errors.Add(string.Format(
                                    Messages.EncryptionErrorFormat, file, ex.Message));
                            }
                            else
                            {
                                Interlocked.CompareExchange(ref fatalError, ex.Message, null);
                                await linkedCts.CancelAsync();
                            }
                        }
                    });
            }
            catch (OperationCanceledException) when (fatalError is not null)
            {
                stopwatch.Stop();
                return Result<BackupResult>.Failure(fatalError);
            }

            ManifestHeader header = new(
                request.EncryptionAlgorithm,
                request.KeyDerivationAlgorithm,
                request.Compression);

            ChunkManifestData manifestData = new(
                header,
                Convert.ToBase64String(masterSalt),
                [.. fileEntries]);

            var manifestErrors = await manifestService.SaveChunkManifestAsync(
                manifestData,
                destinationPath,
                encryptionKey,
                request.EncryptionAlgorithm,
                cancellationToken);

            List<string> errorList = [.. errors];
            errorList.AddRange(manifestErrors);

            stopwatch.Stop();
            var isSuccess = errorList.Count == 0 && processedFiles == totalFiles;

            return errorList.Count > 0 && processedFiles == 0
                ? Result<BackupResult>.Failure(
                    string.Format(Messages.AllFilesFailedFormat, string.Join("; ", errorList)))
                : Result<BackupResult>.Success(
                    new BackupResult(isSuccess, stopwatch.Elapsed, totalBytes,
                        processedFiles, totalFiles, errors: errorList));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(masterKey);
            CryptographicOperations.ZeroMemory(encryptionKey);
            CryptographicOperations.ZeroMemory(namingKey);
        }
    }

    public async Task<Result<BackupResult>> UpdateAsync(
        string sourcePath,
        string destinationPath,
        BackupRequest request,
        IProgress<BackupStatus> progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        var preamble = await manifestService.ReadChunkManifestPreambleAsync(
            destinationPath,
            cancellationToken);

        if (preamble is null)
        {
            return Result<BackupResult>.Failure(Messages.ManifestRequiredForUpdate);
        }

        request = request with
        {
            EncryptionAlgorithm = preamble.Algorithm,
            KeyDerivationAlgorithm = preamble.KeyDerivation,
        };

        var masterSalt = preamble.MasterSalt;
        var masterKey = DeriveKey(request.Password, masterSalt, request.KeyDerivationAlgorithm);
        var encryptionKey = DeriveSubKey(masterKey, "chunk-encryption"u8);
        var namingKey = DeriveSubKey(masterKey, "chunk-naming"u8);

        var existingManifest = manifestService.DecryptChunkManifest(preamble, encryptionKey);
        if (existingManifest is null)
        {
            return Result<BackupResult>.Failure(Messages.ManifestRequiredForUpdate);
        }

        request = request with
        {
            Compression = existingManifest.Header.Compression,
        };

        try
        {
            var encryptionStrategy = ResolveEncryptionStrategy(request.EncryptionAlgorithm);
            ICompressionStrategy? compressionStrategy = null;
            if (request.Compression != CompressionMode.None)
            {
                compressionStrategy = compressionServiceFactory.Create(request.Compression);
            }

            var chunksDir = fileOperationsService.CombinePath(
                destinationPath, BackupConstants.ChunksDirectoryName);
            await fileOperationsService.CreateDirectoryAsync(chunksDir, cancellationToken);

            // Build index of existing files and their chunk hashes
            Dictionary<string, ChunkManifestFileEntry> existingFileIndex = new(
                StringComparer.OrdinalIgnoreCase);
            HashSet<string> existingChunkHashes = new(StringComparer.Ordinal);

            foreach (var entry in existingManifest.Files)
            {
                existingFileIndex[entry.OriginalPath] = entry;
                foreach (var chunk in entry.Chunks)
                {
                    existingChunkHashes.Add(chunk.Hash);
                }
            }

            var sourceFiles = await fileOperationsService.GetFilesAsync(
                sourcePath, "*.*", cancellationToken);

            ConcurrentBag<ChunkManifestFileEntry> updatedEntries = [];
            ConcurrentBag<string> errors = [];
            var processedFiles = 0;
            long processedBytes = 0;
            string? fatalError = null;

            HashSet<string> sourceRelativePaths = new(StringComparer.OrdinalIgnoreCase);
            List<(string File, string RelativePath, long Size)> filesToProcess = [];
            HashSet<string> referencedChunkHashes = new(StringComparer.Ordinal);

            // Determine which files need processing
            foreach (var file in sourceFiles)
            {
                var relativePath = fileOperationsService.GetRelativePath(sourcePath, file);
                sourceRelativePaths.Add(relativePath);
                var fileSize = fileOperationsService.GetFileSize(file);

                if (existingFileIndex.TryGetValue(relativePath, out var existing))
                {
                    var currentHash = await fileOperationsService.ComputeFileHashAsync(
                        file, cancellationToken);

                    if (string.Equals(currentHash, existing.FileHash, StringComparison.Ordinal))
                    {
                        // File unchanged - keep existing entry
                        updatedEntries.Add(existing);
                        foreach (var chunk in existing.Chunks)
                        {
                            referencedChunkHashes.Add(chunk.Hash);
                        }

                        continue;
                    }
                }

                filesToProcess.Add((file, relativePath, fileSize));
            }

            var totalFilesToProcess = filesToProcess.Count;
            var totalBytes = filesToProcess.Sum(f => f.Size);

            progress?.Report(new BackupStatus(0, totalFilesToProcess, 0, totalBytes, TimeSpan.Zero));

            // Re-chunk changed/new files, reusing existing chunks where possible
            ConcurrentDictionary<string, bool> storedChunks = new(StringComparer.Ordinal);
            foreach (var hash in existingChunkHashes)
            {
                storedChunks.TryAdd(hash, true);
            }

            if (totalFilesToProcess > 0)
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);

                try
                {
                    await Parallel.ForEachAsync(
                        filesToProcess,
                        new ParallelOptions
                        {
                            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount),
                            CancellationToken = linkedCts.Token,
                        },
                        async (fileItem, token) =>
                        {
                            try
                            {
                                var entry = await ChunkAndEncryptFileAsync(
                                    fileItem.File,
                                    fileItem.RelativePath,
                                    fileItem.Size,
                                    chunksDir,
                                    encryptionKey,
                                    namingKey,
                                    encryptionStrategy,
                                    compressionStrategy,
                                    storedChunks,
                                    token);

                                updatedEntries.Add(entry);
                                foreach (var chunk in entry.Chunks)
                                {
                                    referencedChunkHashes.Add(chunk.Hash);
                                }

                                Interlocked.Increment(ref processedFiles);
                                var currentBytes = Interlocked.Add(
                                    ref processedBytes, fileItem.Size);

                                progress?.Report(new BackupStatus(
                                    Volatile.Read(ref processedFiles),
                                    totalFilesToProcess,
                                    currentBytes,
                                    totalBytes,
                                    stopwatch.Elapsed));
                            }
                            catch (Exception ex) when (ex is not OperationCanceledException)
                            {
                                if (IsFileLevelError(ex))
                                {
                                    errors.Add(string.Format(
                                        Messages.EncryptionErrorFormat, fileItem.File, ex.Message));
                                }
                                else
                                {
                                    Interlocked.CompareExchange(ref fatalError, ex.Message, null);
                                    await linkedCts.CancelAsync();
                                }
                            }
                        });
                }
                catch (OperationCanceledException) when (fatalError is not null)
                {
                    stopwatch.Stop();
                    return Result<BackupResult>.Failure(fatalError);
                }
            }

            // Delete orphaned chunks
            await DeleteOrphanedChunksAsync(
                chunksDir, referencedChunkHashes, namingKey, cancellationToken);

            // Save updated manifest
            ManifestHeader header = new(
                request.EncryptionAlgorithm,
                request.KeyDerivationAlgorithm,
                request.Compression);

            ChunkManifestData newManifest = new(
                header,
                existingManifest.MasterSalt,
                [.. updatedEntries]);

            var manifestErrors = await manifestService.SaveChunkManifestAsync(
                newManifest,
                destinationPath,
                encryptionKey,
                request.EncryptionAlgorithm,
                cancellationToken);

            List<string> errorList = [.. errors];
            errorList.AddRange(manifestErrors);

            stopwatch.Stop();
            var isSuccess = errorList.Count == 0 && processedFiles == totalFilesToProcess;

            return Result<BackupResult>.Success(
                new BackupResult(isSuccess, stopwatch.Elapsed, totalBytes,
                    processedFiles, totalFilesToProcess, errors: errorList));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(masterKey);
            CryptographicOperations.ZeroMemory(encryptionKey);
            CryptographicOperations.ZeroMemory(namingKey);
        }
    }

    public async Task<Result<BackupResult>> RestoreAsync(
        string sourcePath,
        string destinationPath,
        BackupRequest request,
        IProgress<BackupStatus> progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        var preamble = await manifestService.ReadChunkManifestPreambleAsync(
            sourcePath,
            cancellationToken);
        if (preamble is null)
        {
            return Result<BackupResult>.Failure(Messages.ManifestRequiredForDecryption);
        }

        var masterKey = DeriveKey(request.Password, preamble.MasterSalt, preamble.KeyDerivation);
        var encryptionKey = DeriveSubKey(masterKey, "chunk-encryption"u8);
        var namingKey = DeriveSubKey(masterKey, "chunk-naming"u8);

        var manifest = manifestService.DecryptChunkManifest(preamble, encryptionKey);
        if (manifest is null)
        {
            return Result<BackupResult>.Failure(Messages.ManifestRequiredForDecryption);
        }

        try
        {
            var encryptionStrategy = ResolveEncryptionStrategy(
                manifest.Header.EncryptionAlgorithm);
            ICompressionStrategy? compressionStrategy = null;
            if (manifest.Header.Compression != CompressionMode.None)
            {
                compressionStrategy = compressionServiceFactory.Create(
                    manifest.Header.Compression);
            }

            var chunksDir = fileOperationsService.CombinePath(
                sourcePath, BackupConstants.ChunksDirectoryName);

            var totalFiles = manifest.Files.Count;
            var totalBytes = manifest.Files.Sum(f => f.TotalSize);
            progress?.Report(new BackupStatus(0, totalFiles, 0, totalBytes, TimeSpan.Zero));

            ConcurrentBag<string> errors = [];
            long processedBytes = 0;
            var processedFiles = 0;
            string? fatalError = null;

            await fileOperationsService.CreateDirectoryAsync(
                destinationPath, cancellationToken);

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

            try
            {
                await Parallel.ForEachAsync(
                    manifest.Files,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount),
                        CancellationToken = linkedCts.Token,
                    },
                    async (fileEntry, token) =>
                    {
                        try
                        {
                            await RestoreFileFromChunksAsync(
                                fileEntry,
                                chunksDir,
                                destinationPath,
                                encryptionKey,
                                namingKey,
                                encryptionStrategy,
                                compressionStrategy,
                                token);

                            Interlocked.Increment(ref processedFiles);
                            var currentBytes = Interlocked.Add(
                                ref processedBytes, fileEntry.TotalSize);

                            progress?.Report(new BackupStatus(
                                Volatile.Read(ref processedFiles),
                                totalFiles,
                                currentBytes,
                                totalBytes,
                                stopwatch.Elapsed));
                        }
                        catch (CryptographicException)
                        {
                            Interlocked.CompareExchange(
                                ref fatalError,
                                Domain.Resources.Messages.InvalidPassword,
                                null);
                            await linkedCts.CancelAsync();
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            if (IsFileLevelError(ex))
                            {
                                errors.Add(string.Format(
                                    Messages.EncryptionErrorFormat,
                                    fileEntry.OriginalPath, ex.Message));
                            }
                            else
                            {
                                Interlocked.CompareExchange(
                                    ref fatalError, ex.Message, null);
                                await linkedCts.CancelAsync();
                            }
                        }
                    });
            }
            catch (OperationCanceledException) when (fatalError is not null)
            {
                stopwatch.Stop();
                return Result<BackupResult>.Failure(fatalError);
            }

            List<string> errorList = [.. errors];
            stopwatch.Stop();
            var isSuccess = errorList.Count == 0 && processedFiles == totalFiles;

            return errorList.Count > 0 && processedFiles == 0
                ? Result<BackupResult>.Failure(
                    string.Format(Messages.AllFilesFailedFormat, string.Join("; ", errorList)))
                : Result<BackupResult>.Success(
                    new BackupResult(isSuccess, stopwatch.Elapsed, totalBytes,
                        processedFiles, totalFiles, errors: errorList));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(masterKey);
            CryptographicOperations.ZeroMemory(encryptionKey);
            CryptographicOperations.ZeroMemory(namingKey);
        }
    }

    private async Task<ChunkManifestFileEntry> ChunkAndEncryptFileAsync(
        string filePath,
        string relativePath,
        long fileSize,
        string chunksDir,
        byte[] encryptionKey,
        byte[] namingKey,
        IEncryptionAlgorithmStrategy encryptionStrategy,
        ICompressionStrategy? compressionStrategy,
        ConcurrentDictionary<string, bool> storedChunks,
        CancellationToken cancellationToken)
    {
        List<ChunkManifestChunkRef> chunkRefs = [];

        await using var fileStream = fileOperationsService.OpenReadStream(
            filePath, BackupIOConstants.CopyBufferSize);

        using var fileHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        await foreach (var chunkData in chunkingStrategy.ChunkAsync(fileStream, cancellationToken))
        {
            fileHasher.AppendData(chunkData.Span);

            var chunkHash = SHA256.HashData(chunkData.Span);
            var chunkHashB64 = Convert.ToBase64String(chunkHash);
            var chunkFileName = ComputeChunkFileName(namingKey, chunkHash);
            var chunkFilePath = fileOperationsService.CombinePath(
                chunksDir, chunkFileName + BackupConstants.AppFileExtension);

            var nonce = GenerateNonce();
            var nonceB64 = Convert.ToBase64String(nonce);

            if (storedChunks.TryAdd(chunkHashB64, true))
            {
                byte[] dataToEncrypt;
                if (compressionStrategy is not null)
                {
                    await using MemoryStream inputStream = new(
                        chunkData.ToArray(), writable: false);
                    await using var compressedStream = await compressionStrategy.CompressAsync(
                        inputStream, cancellationToken);
                    await using MemoryStream compressedBuffer = new();
                    await compressedStream.CopyToAsync(compressedBuffer, cancellationToken);
                    dataToEncrypt = compressedBuffer.ToArray();
                }
                else
                {
                    dataToEncrypt = chunkData.ToArray();
                }

                try
                {
                    var associatedData = BuildChunkAssociatedData(chunkHash, nonce);
                    var encrypted = encryptionStrategy.EncryptChunk(
                        dataToEncrypt, encryptionKey, nonce, associatedData);

                    await fileOperationsService.WriteAllBytesAsync(
                        chunkFilePath, encrypted, cancellationToken);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(dataToEncrypt);
                }
            }

            chunkRefs.Add(new ChunkManifestChunkRef(chunkHashB64, chunkData.Length, nonceB64));
        }

        var fileHash = fileHasher.GetHashAndReset();
        var fileHashB64 = Convert.ToBase64String(fileHash);

        return new ChunkManifestFileEntry(relativePath, fileHashB64, fileSize, chunkRefs);
    }

    private async Task RestoreFileFromChunksAsync(
        ChunkManifestFileEntry fileEntry,
        string chunksDir,
        string destinationPath,
        byte[] encryptionKey,
        byte[] namingKey,
        IEncryptionAlgorithmStrategy encryptionStrategy,
        ICompressionStrategy? compressionStrategy,
        CancellationToken cancellationToken)
    {
        var destFilePath = fileOperationsService.CombinePath(
            destinationPath, fileEntry.OriginalPath);
        var destDir = fileOperationsService.GetDirectoryName(destFilePath);

        if (!string.IsNullOrEmpty(destDir))
        {
            await fileOperationsService.CreateDirectoryAsync(destDir, cancellationToken);
        }

        await using var destStream = fileOperationsService.CreateWriteStream(
            destFilePath, BackupIOConstants.CopyBufferSize);

        foreach (var chunkRef in fileEntry.Chunks)
        {
            var chunkHash = Convert.FromBase64String(chunkRef.Hash);
            var nonce = Convert.FromBase64String(chunkRef.Nonce);
            var chunkFileName = ComputeChunkFileName(namingKey, chunkHash);
            var chunkFilePath = fileOperationsService.CombinePath(
                chunksDir, chunkFileName + BackupConstants.AppFileExtension);

            var encryptedData = await fileOperationsService.ReadAllBytesAsync(
                chunkFilePath, cancellationToken);

            var associatedData = BuildChunkAssociatedData(chunkHash, nonce);
            var decryptedData = encryptionStrategy.DecryptChunk(
                encryptedData, encryptionKey, nonce, associatedData);

            try
            {
                if (compressionStrategy is not null)
                {
                    await using MemoryStream compressedStream = new(
                        decryptedData, writable: false);
                    await using var decompressedStream =
                        await compressionStrategy.DecompressAsync(
                            compressedStream, cancellationToken);
                    await decompressedStream.CopyToAsync(destStream, cancellationToken);
                }
                else
                {
                    await destStream.WriteAsync(decryptedData, cancellationToken);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(decryptedData);
            }
        }
    }

    private IEncryptionAlgorithmStrategy ResolveEncryptionStrategy(
        EncryptionAlgorithm algorithm)
    {
        return !encryptionStrategiesById.TryGetValue(algorithm, out var strategy)
            ? throw new ArgumentOutOfRangeException(
                nameof(algorithm),
                string.Format(
                    BackupZCrypt.Domain.Resources.Messages.EncryptionAlgorithmNotRegisteredFormat,
                    algorithm))
            : strategy;
    }

    private async Task DeleteOrphanedChunksAsync(
        string chunksDir,
        HashSet<string> referencedChunkHashes,
        byte[] namingKey,
        CancellationToken cancellationToken)
    {
        try
        {
            // Build set of expected file names from referenced hashes
            HashSet<string> expectedFileNames = new(StringComparer.OrdinalIgnoreCase);
            foreach (var hash in referencedChunkHashes)
            {
                var hashBytes = Convert.FromBase64String(hash);
                var fileName = ComputeChunkFileName(namingKey, hashBytes)
                    + BackupConstants.AppFileExtension;
                expectedFileNames.Add(fileName);
            }

            if (!fileOperationsService.DirectoryExists(chunksDir))
            {
                return;
            }

            var existingFiles = await fileOperationsService.GetFilesAsync(
                chunksDir, "*" + BackupConstants.AppFileExtension, cancellationToken);

            foreach (var file in existingFiles)
            {
                var fileName = Path.GetFileName(file);
                if (!expectedFileNames.Contains(fileName))
                {
                    try
                    {
                        fileOperationsService.DeleteFile(file);
                    }
                    catch
                    {
                        // Best-effort cleanup
                    }
                }
            }
        }
        catch
        {
            // Best-effort cleanup
        }
    }

    private byte[] DeriveKey(string password, byte[] salt, KeyDerivationAlgorithm kdf)
    {
        var strategy = keyDerivationServiceFactory.Create(kdf);
        return strategy.DeriveKey(password, salt, KeySizeBytes * 8);
    }

    private static byte[] DeriveSubKey(byte[] masterKey, ReadOnlySpan<byte> context)
    {
        return HKDF.Expand(HashAlgorithmName.SHA256, masterKey, KeySizeBytes, context.ToArray());
    }

    private static byte[] GenerateSalt()
    {
        var salt = new byte[SaltSizeBytes];
        RandomNumberGenerator.Fill(salt);
        return salt;
    }

    private static byte[] GenerateNonce()
    {
        var nonce = new byte[NonceSizeBytes];
        RandomNumberGenerator.Fill(nonce);
        return nonce;
    }

    private static string ComputeChunkFileName(byte[] namingKey, byte[] chunkHash)
    {
        var hmac = HMACSHA256.HashData(namingKey, chunkHash);
        return Convert.ToHexStringLower(hmac);
    }

    private static byte[] BuildChunkAssociatedData(byte[] chunkHash, byte[] nonce)
    {
        var ad = new byte[chunkHash.Length + nonce.Length];
        chunkHash.CopyTo(ad, 0);
        nonce.CopyTo(ad, chunkHash.Length);
        return ad;
    }

    private static bool IsFileLevelError(Exception ex)
    {
        return ex is FileNotFoundException
            or IOException { Message: not null }
            or UnauthorizedAccessException;
    }
}
