using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.Utilities.Formatters;
using BackupZCrypt.Application.Utilities.Helpers;
using BackupZCrypt.Application.Validators.Interfaces;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Services.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Backup;
using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Application.Validators;

/// <summary>
/// Validates backup requests against the file system and storage, collecting blocking errors
/// (invalid paths, missing sources, weak passwords) and advisory warnings (low disk space,
/// large operations, existing destination files).
/// </summary>
/// <param name="fileOperations">Service used to inspect files and directories.</param>
/// <param name="systemStorage">Service used to query drive readiness and free space.</param>
/// <param name="passwordService">Service used to assess password strength for warnings.</param>
internal sealed class BackupRequestValidator(
    IFileOperationsService fileOperations,
    ISystemStorageService systemStorage,
    IPasswordService passwordService
) : IBackupRequestValidator
{
    /// <summary>
    /// Analyzes a request for blocking errors such as invalid or missing paths, password problems,
    /// and source/destination overlap.
    /// </summary>
    /// <param name="request">The backup request to validate.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The localizable errors found; empty when the request is valid.</returns>
    public async Task<IReadOnlyList<LocalizableMessage>> AnalyzeErrorsAsync(
        BackupRequest request,
        CancellationToken cancellationToken = default
    )
    {
        List<LocalizableMessage> errors = [];

        var sourcePath = PathNormalizationHelper.TryNormalize(
            request.SourcePath,
            out var sourceNormalizeError
        );
        var destinationPath = PathNormalizationHelper.TryNormalize(
            request.DestinationPath,
            out var destinationNormalizeError
        );

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
            errors.Add(new LocalizableMessage(MessageCode.SourcePathEmpty));
        }
        else if (fileOperations.FileExists(sourcePath))
        {
            errors.Add(new LocalizableMessage(MessageCode.SourceMustBeDirectory));
        }
        else if (!fileOperations.DirectoryExists(sourcePath))
        {
            errors.Add(new LocalizableMessage(MessageCode.SourcePathNotExistFormat, sourcePath));
        }
        else
        {
            try
            {
                var files = await fileOperations.GetFilesAsync(sourcePath, "*", cancellationToken);
                if (files.Length == 0)
                {
                    errors.Add(new LocalizableMessage(MessageCode.SourceDirectoryEmpty));
                }
            }
            catch (UnauthorizedAccessException)
            {
                errors.Add(new LocalizableMessage(MessageCode.SourceAccessDenied));
            }
            catch (Exception ex)
            {
                errors.Add(new LocalizableMessage(MessageCode.SourceAccessErrorFormat, ex.Message));
            }
        }

        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            errors.Add(new LocalizableMessage(MessageCode.DestinationPathEmpty));
        }
        else
        {
            try
            {
                var drive = systemStorage.GetPathRoot(destinationPath);

                if (!string.IsNullOrEmpty(drive) && !systemStorage.IsDriveReady(drive))
                {
                    errors.Add(
                        new LocalizableMessage(
                            MessageCode.DestinationDriveNotAccessibleFormat,
                            drive
                        )
                    );
                }
            }
            catch (Exception ex)
            {
                errors.Add(
                    new LocalizableMessage(MessageCode.DestinationInvalidFormat, ex.Message)
                );
            }
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            errors.Add(new LocalizableMessage(MessageCode.PasswordRequired));
        }
        else
        {
            if (request.Password.Length < 8)
            {
                errors.Add(new LocalizableMessage(MessageCode.PasswordTooShort));
            }

            if (request.Password.Length > 1000)
            {
                errors.Add(new LocalizableMessage(MessageCode.PasswordTooLong));
            }

            if (request.Password.Trim() != request.Password)
            {
                errors.Add(new LocalizableMessage(MessageCode.PasswordLeadingTrailingSpaces));
            }
        }

        if (request.Operation == BackupOperation.Create)
        {
            if (string.IsNullOrWhiteSpace(request.ConfirmPassword))
            {
                errors.Add(new LocalizableMessage(MessageCode.ConfirmPasswordRequired));
            }
            else if (
                !string.Equals(request.Password, request.ConfirmPassword, StringComparison.Ordinal)
            )
            {
                errors.Add(new LocalizableMessage(MessageCode.PasswordMismatch));
            }
        }

        if (!string.IsNullOrWhiteSpace(sourcePath) && !string.IsNullOrWhiteSpace(destinationPath))
        {
            try
            {
                if (fileOperations.DirectoryExists(sourcePath))
                {
                    if (
                        string.Equals(
                            sourcePath,
                            destinationPath,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        errors.Add(
                            new LocalizableMessage(MessageCode.SourceDestinationSameDirectory)
                        );
                    }
                    else if (
                        destinationPath.StartsWith(
                            sourcePath + Path.DirectorySeparatorChar,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        errors.Add(new LocalizableMessage(MessageCode.DestinationInsideSource));
                    }
                    else if (
                        sourcePath.StartsWith(
                            destinationPath + Path.DirectorySeparatorChar,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        errors.Add(new LocalizableMessage(MessageCode.SourceInsideDestination));
                    }
                }
            }
            catch
            {
            }
        }

        return errors;
    }

    /// <summary>
    /// Analyzes a request for advisory warnings such as low disk space, large file counts,
    /// existing destination files, and weak passwords.
    /// </summary>
    /// <param name="request">The backup request to inspect.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The localizable warnings found; empty when there are none.</returns>
    public async Task<IReadOnlyList<LocalizableMessage>> AnalyzeWarningsAsync(
        BackupRequest request,
        CancellationToken cancellationToken = default
    )
    {
        List<LocalizableMessage> warnings = [];

        var sourcePath = PathNormalizationHelper.TryNormalize(request.SourcePath, out _);
        var destinationPath = PathNormalizationHelper.TryNormalize(request.DestinationPath, out _);
        if (sourcePath is null || destinationPath is null)
        {
            return warnings;
        }

        try
        {
            if (fileOperations.DirectoryExists(sourcePath))
            {
                var sourceFiles = await fileOperations.GetFilesAsync(
                    sourcePath,
                    "*",
                    cancellationToken
                );

                var destinationDrive = systemStorage.GetPathRoot(destinationPath);
                if (
                    !string.IsNullOrEmpty(destinationDrive)
                    && systemStorage.IsDriveReady(destinationDrive)
                )
                {
                    var totalSize = sourceFiles.Sum(f =>
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
                    var available = systemStorage.GetAvailableFreeSpace(destinationDrive);
                    if (available >= 0 && available < requiredSpace)
                    {
                        warnings.Add(
                            new LocalizableMessage(
                                MessageCode.LowDiskSpaceFormat,
                                ByteSizeFormatter.Format(available),
                                ByteSizeFormatter.Format(requiredSpace)
                            )
                        );
                    }
                }

                var fileCount = sourceFiles.Length;
                if (fileCount > 10000)
                {
                    warnings.Add(
                        new LocalizableMessage(
                            MessageCode.LargeOperationFormat,
                            fileCount.ToString("N0")
                        )
                    );
                }
                else if (fileCount > 1000)
                {
                    warnings.Add(
                        new LocalizableMessage(
                            MessageCode.MediumOperationFormat,
                            fileCount.ToString("N0")
                        )
                    );
                }
            }

            var hasExistingFiles = false;
            var existingFileCount = 0;

            if (fileOperations.DirectoryExists(destinationPath))
            {
                var existingFiles = await fileOperations.GetFilesAsync(
                    destinationPath,
                    "*",
                    cancellationToken
                );
                if (existingFiles.Length > 0)
                {
                    hasExistingFiles = true;
                    existingFileCount = existingFiles.Length;
                }
            }

            if (hasExistingFiles && request.Operation == BackupOperation.Restore)
            {
                warnings.Add(
                    new LocalizableMessage(
                        MessageCode.DestinationExistingFilesFormat,
                        existingFileCount.ToString("N0")
                    )
                );
            }

            if (request.Operation == BackupOperation.Create)
            {
                var strength = passwordService.AnalyzePasswordStrength(request.Password);
                if (strength.Score < 60)
                {
                    warnings.Add(new LocalizableMessage(MessageCode.WeakPasswordWarning));
                }
            }
        }
        catch
        {
        }

        return warnings;
    }
}
