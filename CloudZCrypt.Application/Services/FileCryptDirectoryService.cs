using System.Diagnostics;
using CloudZCrypt.Application.Services.Interfaces;
using CloudZCrypt.Application.ValueObjects;
using CloudZCrypt.Application.ValueObjects.Manifest;
using CloudZCrypt.Domain.Constants;
using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Factories.Interfaces;
using CloudZCrypt.Domain.Services.Interfaces;
using CloudZCrypt.Domain.Strategies.Interfaces;
using CloudZCrypt.Domain.ValueObjects.FileCrypt;

namespace CloudZCrypt.Application.Services;

internal sealed class FileCryptDirectoryService(
    IEncryptionServiceFactory encryptionServiceFactory,
    INameObfuscationServiceFactory nameObfuscationServiceFactory,
    IFileOperationsService fileOperations,
    IManifestService manifestService
) : IFileCryptDirectoryService
{
    public async Task<Result<FileCryptResult>> ProcessAsync(
        string sourcePath,
        string destinationPath,
        FileCryptRequest request,
        IProgress<FileCryptStatus> progress,
        CancellationToken cancellationToken
    )
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        List<string> errors = [];

        IEncryptionAlgorithmStrategy encryptionService = encryptionServiceFactory.Create(
            request.EncryptionAlgorithm
        );
        INameObfuscationStrategy obfuscationService = nameObfuscationServiceFactory.Create(
            request.NameObfuscation
        );

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
                    errors: ["No files found in the source directory."]
                )
            );
        }

        List<ManifestEntry> manifestEntries = [];
        Dictionary<string, string>? manifestMap = null;

        if (request.Operation == EncryptOperation.Decrypt)
        {
            manifestMap = await manifestService.TryReadManifestAsync(
                sourcePath,
                encryptionService,
                request,
                cancellationToken
            );
        }

        string manifestEncryptedAbsolute = Path.Combine(
            sourcePath,
            FileCryptConstants.ManifestFileName
        );
        string manifestEncryptedRelative = fileOperations.GetRelativePath(
            sourcePath,
            manifestEncryptedAbsolute
        );

        string[] filesToProcess = files;
        if (request.Operation == EncryptOperation.Decrypt)
        {
            filesToProcess = files
                .Where(f =>
                    !string.Equals(
                        fileOperations.GetRelativePath(sourcePath, f),
                        manifestEncryptedRelative,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                .ToArray();
        }

        if (request.Operation == EncryptOperation.Encrypt)
        {
            await fileOperations.CreateDirectoryAsync(destinationPath, cancellationToken);
        }

        long totalBytes = filesToProcess.Sum(fileOperations.GetFileSize);
        long processedBytes = 0;
        int processedFiles = 0;

        Dictionary<string, string> directoryObfuscationCache = new(
            StringComparer.OrdinalIgnoreCase
        );

        progress?.Report(
            new FileCryptStatus(0, filesToProcess.Length, 0, totalBytes, TimeSpan.Zero)
        );

        for (int i = 0; i < filesToProcess.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string file = filesToProcess[i];

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
                    directoryObfuscationCache
                );

                string obfuscatedRelativePath = fileOperations.GetRelativePath(
                    destinationPath,
                    destinationFilePath
                );
                manifestEntries.Add(new ManifestEntry(relativePath, obfuscatedRelativePath));
            }
            else
            {
                destinationFilePath = fileOperations.CombinePath(
                    destinationPath,
                    relativePath.Replace(FileCryptConstants.AppFileExtension, "")
                );

                if (
                    manifestMap is not null
                    && manifestMap.TryGetValue(relativePath, out string? originalRelativePath)
                )
                {
                    destinationFilePath = fileOperations.CombinePath(
                        destinationPath,
                        originalRelativePath
                    );
                }
            }

            string? destDir = fileOperations.GetDirectoryName(destinationFilePath);
            if (!string.IsNullOrEmpty(destDir))
            {
                await fileOperations.CreateDirectoryAsync(destDir, cancellationToken);
            }

            try
            {
                bool operationResult = await ProcessSingleFileAsync(
                    encryptionService,
                    file,
                    destinationFilePath,
                    request,
                    cancellationToken
                );
                if (operationResult)
                {
                    processedFiles++;
                }
            }
            catch (Domain.Exceptions.EncryptionAccessDeniedException ex)
            {
                stopwatch.Stop();
                return Result<FileCryptResult>.Failure(
                    $"Operation stopped due to access denied error: {ex.Message}"
                );
            }
            catch (Domain.Exceptions.EncryptionInsufficientSpaceException ex)
            {
                stopwatch.Stop();
                return Result<FileCryptResult>.Failure(
                    $"Operation stopped due to insufficient disk space: {ex.Message}"
                );
            }
            catch (Domain.Exceptions.EncryptionInvalidPasswordException ex)
            {
                stopwatch.Stop();
                return Result<FileCryptResult>.Failure(
                    $"Operation stopped due to invalid password: {ex.Message}"
                );
            }
            catch (Domain.Exceptions.EncryptionKeyDerivationException ex)
            {
                stopwatch.Stop();
                return Result<FileCryptResult>.Failure(
                    $"Operation stopped due to key derivation error: {ex.Message}"
                );
            }
            catch (Domain.Exceptions.EncryptionFileNotFoundException ex)
            {
                errors.Add($"File not found (skipped): {file} - {ex.Message}");
            }
            catch (Domain.Exceptions.EncryptionCorruptedFileException ex)
            {
                errors.Add($"Corrupted file (skipped): {file} - {ex.Message}");
            }
            catch (Domain.Exceptions.EncryptionCipherException ex)
            {
                errors.Add($"Cipher error for file: {file} - {ex.Message}");
            }
            catch (Domain.Exceptions.EncryptionException ex)
            {
                errors.Add($"Encryption error for file: {file} - {ex.Message}");
            }

            long fileSize = 0;
            try
            {
                fileSize = fileOperations.GetFileSize(file);
            }
            catch
            {
                // ignore
            }

            processedBytes += fileSize;
            progress?.Report(
                new FileCryptStatus(
                    i + 1,
                    filesToProcess.Length,
                    processedBytes,
                    totalBytes,
                    stopwatch.Elapsed
                )
            );
        }

        if (request.Operation == EncryptOperation.Encrypt && manifestEntries.Count > 0)
        {
            IReadOnlyList<string> manifestErrors = await manifestService.TrySaveManifestAsync(
                manifestEntries,
                destinationPath,
                encryptionService,
                request,
                cancellationToken
            );
            if (manifestErrors.Count > 0)
            {
                errors.AddRange(manifestErrors);
            }
        }

        stopwatch.Stop();
        bool isSuccess = errors.Count == 0 && processedFiles == filesToProcess.Length;

        return errors.Count > 0 && processedFiles == 0
            ? Result<FileCryptResult>.Failure(
                $"Failed to process any files. Errors: {string.Join("; ", errors)}"
            )
            : Result<FileCryptResult>.Success(
                new FileCryptResult(
                    isSuccess,
                    stopwatch.Elapsed,
                    totalBytes,
                    processedFiles,
                    filesToProcess.Length,
                    errors: errors
                )
            );
    }

    private string ObfuscateFullPath(
        string sourcePath,
        string sourceFilePath,
        string relativePath,
        string destinationRoot,
        INameObfuscationStrategy obfuscationService,
        Dictionary<string, string> directoryCache
    )
    {
        string[] segments = relativePath.Split(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar
        );

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
                    filenameWithExtension
                );
                obfuscatedSegments.Add(obfuscatedFilename);
            }
            else
            {
                currentSourcePath = Path.Combine(currentSourcePath, segment);
                string directoryKey = fileOperations.GetRelativePath(sourcePath, currentSourcePath);

                if (!directoryCache.TryGetValue(directoryKey, out string? obfuscatedDirName))
                {
                    obfuscatedDirName = obfuscationService.ObfuscateFileName(
                        currentSourcePath,
                        segment
                    );
                    directoryCache[directoryKey] = obfuscatedDirName;
                }

                obfuscatedSegments.Add(obfuscatedDirName);
            }
        }

        return fileOperations.CombinePath([destinationRoot, .. obfuscatedSegments]);
    }

    private static Task<bool> ProcessSingleFileAsync(
        IEncryptionAlgorithmStrategy encryptionService,
        string sourceFile,
        string destinationFile,
        FileCryptRequest request,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        return request.Operation switch
        {
            EncryptOperation.Encrypt => encryptionService.EncryptFileAsync(
                sourceFile,
                destinationFile,
                request.Password,
                request.KeyDerivationAlgorithm,
                request.Compression
            ),
            EncryptOperation.Decrypt => encryptionService.DecryptFileAsync(
                sourceFile,
                destinationFile,
                request.Password,
                request.KeyDerivationAlgorithm
            ),
            _ => throw new NotSupportedException($"Unsupported operation: {request.Operation}"),
        };
    }
}
