namespace BackupZCrypt.Application.Validators;

using BackupZCrypt.Application.Resources;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.Utilities.Formatters;
using BackupZCrypt.Application.Utilities.Helpers;
using BackupZCrypt.Application.Validators.Interfaces;
using BackupZCrypt.Application.ValueObjects.Password;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Services.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Backup;

internal sealed class BackupRequestValidator(
    IFileOperationsService fileOperations,
    ISystemStorageService systemStorage,
    IPasswordService passwordService) : IBackupRequestValidator
{
    public async Task<IReadOnlyList<string>> AnalyzeErrorsAsync(
        BackupRequest request,
        CancellationToken cancellationToken = default)
    {
        List<string> errors = [];

        string? sourcePath = PathNormalizationHelper.TryNormalize(
            request.SourcePath,
            out string? sourceNormalizeError);
        string? destinationPath = PathNormalizationHelper.TryNormalize(
            request.DestinationPath,
            out string? destinationNormalizeError);

        if (sourceNormalizeError is not null)
        {
            errors.Add(sourceNormalizeError);
        }

        if (destinationNormalizeError is not null)
        {
            errors.Add(destinationNormalizeError);
        }

        if (sourcePath is null || destinationPath is null)
        {
            return errors;
        }

        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            errors.Add(Messages.SourcePathEmpty);
        }
        else if (
            !fileOperations.FileExists(sourcePath) && !fileOperations.DirectoryExists(sourcePath))
        {
            errors.Add(string.Format(Messages.SourcePathNotExistFormat, sourcePath));
        }
        else
        {
            try
            {
                if (fileOperations.FileExists(sourcePath))
                {
                    long fileSize = 0;
                    try
                    {
                        fileSize = fileOperations.GetFileSize(sourcePath);
                    }
                    catch
                    { /* ignore */
                    }

                    if (fileSize == 0)
                    {
                        errors.Add(Messages.SourceFileEmpty);
                    }
                }
                else if (fileOperations.DirectoryExists(sourcePath))
                {
                    string[] files = await fileOperations.GetFilesAsync(
                        sourcePath,
                        "*.*",
                        cancellationToken);
                    if (files.Length == 0)
                    {
                        errors.Add(Messages.SourceDirectoryEmpty);
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                errors.Add(Messages.SourceAccessDenied);
            }
            catch (Exception ex)
            {
                errors.Add(string.Format(Messages.SourceAccessErrorFormat, ex.Message));
            }
        }

        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            errors.Add(Messages.DestinationPathEmpty);
        }
        else
        {
            try
            {
                string? destinationDir = fileOperations.FileExists(sourcePath)
                    ? fileOperations.GetDirectoryName(destinationPath)
                    : destinationPath;

                if (!string.IsNullOrEmpty(destinationDir))
                {
                    string? drive = systemStorage.GetPathRoot(destinationDir);

                    if (!string.IsNullOrEmpty(drive) && !systemStorage.IsDriveReady(drive))
                    {
                        errors.Add(
                            string.Format(Messages.DestinationDriveNotAccessibleFormat, drive));
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add(string.Format(Messages.DestinationInvalidFormat, ex.Message));
            }
        }

        if (request.UseEncryption && string.IsNullOrWhiteSpace(request.Password))
        {
            errors.Add(Messages.PasswordRequired);
        }
        else if (request.UseEncryption)
        {
            if (request.Password.Length < 8)
            {
                errors.Add(Messages.PasswordTooShort);
            }

            if (request.Password.Length > 1000)
            {
                errors.Add(Messages.PasswordTooLong);
            }

            if (request.Password.Trim() != request.Password)
            {
                errors.Add(Messages.PasswordLeadingTrailingSpaces);
            }
        }

        if (request.UseEncryption && request.Operation == EncryptOperation.Encrypt)
        {
            if (string.IsNullOrWhiteSpace(request.ConfirmPassword))
            {
                errors.Add(Messages.ConfirmPasswordRequired);
            }
            else if (
                !string.Equals(request.Password, request.ConfirmPassword, StringComparison.Ordinal))
            {
                errors.Add(Messages.PasswordMismatch);
            }
        }

        if (!string.IsNullOrWhiteSpace(sourcePath) && !string.IsNullOrWhiteSpace(destinationPath))
        {
            try
            {
                if (fileOperations.FileExists(sourcePath))
                {
                    if (
                        string.Equals(
                            sourcePath,
                            destinationPath,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        errors.Add(Messages.SourceDestinationSameFile);
                    }
                }
                else if (fileOperations.DirectoryExists(sourcePath))
                {
                    if (
                        string.Equals(
                            sourcePath,
                            destinationPath,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        errors.Add(Messages.SourceDestinationSameDirectory);
                    }
                    else if (
                        destinationPath.StartsWith(
                            sourcePath + Path.DirectorySeparatorChar,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        errors.Add(Messages.DestinationInsideSource);
                    }
                    else if (
                        sourcePath.StartsWith(
                            destinationPath + Path.DirectorySeparatorChar,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        errors.Add(Messages.SourceInsideDestination);
                    }
                }
            }
            catch
            { /* ignore */
            }
        }

        return errors;
    }

    public async Task<IReadOnlyList<string>> AnalyzeWarningsAsync(
        BackupRequest request,
        CancellationToken cancellationToken = default)
    {
        List<string> warnings = [];

        string? sourcePath = PathNormalizationHelper.TryNormalize(request.SourcePath, out _);
        string? destinationPath = PathNormalizationHelper.TryNormalize(
            request.DestinationPath,
            out _);
        if (sourcePath is null || destinationPath is null)
        {
            return warnings;
        }

        try
        {
            if (fileOperations.DirectoryExists(sourcePath))
            {
                string? destinationDrive = systemStorage.GetPathRoot(destinationPath);
                if (
                    !string.IsNullOrEmpty(destinationDrive)
                    && systemStorage.IsDriveReady(destinationDrive))
                {
                    string[] sourceFiles = await fileOperations.GetFilesAsync(
                        sourcePath,
                        "*.*",
                        cancellationToken);
                    long totalSize = sourceFiles.Sum(f =>
                    {
                        try
                        {
                            return fileOperations.GetFileSize(f);
                        }
                        catch
                        {
                            return 0;
                        }
                    });

                    var requiredSpace = (long)(totalSize * 1.2);
                    long available = systemStorage.GetAvailableFreeSpace(destinationDrive);
                    if (available >= 0 && available < requiredSpace)
                    {
                        warnings.Add(
                            string.Format(
                                Messages.LowDiskSpaceFormat,
                                ByteSizeFormatter.Format(available),
                                ByteSizeFormatter.Format(requiredSpace)));
                    }
                }
            }

            if (fileOperations.DirectoryExists(sourcePath))
            {
                string[] files = await fileOperations.GetFilesAsync(
                    sourcePath,
                    "*.*",
                    cancellationToken);
                int fileCount = files.Length;
                if (fileCount > 10000)
                {
                    warnings.Add(
                        string.Format(Messages.LargeOperationFormat, fileCount.ToString("N0")));
                }
                else if (fileCount > 1000)
                {
                    warnings.Add(
                        string.Format(Messages.MediumOperationFormat, fileCount.ToString("N0")));
                }
            }

            var hasExistingFiles = false;
            int existingFileCount = 0;

            if (fileOperations.FileExists(sourcePath) && fileOperations.FileExists(destinationPath))
            {
                hasExistingFiles = true;
                existingFileCount = 1;
            }
            else if (fileOperations.DirectoryExists(destinationPath))
            {
                string[] existingFiles = await fileOperations.GetFilesAsync(
                    destinationPath,
                    "*.*",
                    cancellationToken);
                if (existingFiles.Length > 0)
                {
                    hasExistingFiles = true;
                    existingFileCount = existingFiles.Length;
                }
            }

            if (hasExistingFiles && request.Operation == EncryptOperation.Decrypt)
            {
                warnings.Add(
                    string.Format(
                        Messages.DestinationExistingFilesFormat,
                        existingFileCount.ToString("N0")));
            }

            if (request.UseEncryption
                && request.Operation == EncryptOperation.Encrypt)
            {
                PasswordStrengthAnalysis strength = passwordService.AnalyzePasswordStrength(
                    request.Password);
                if (strength.Score < 60)
                {
                    warnings.Add(Messages.WeakPasswordWarning);
                }
            }
        }
        catch
        { /* ignore */
        }

        return warnings;
    }
}
