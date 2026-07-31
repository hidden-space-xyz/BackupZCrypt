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
/// (invalid paths, missing sources, unusable passwords) and advisory warnings (low disk space,
/// weak passwords, existing files at the destination).
/// </summary>
/// <param name="fileOperations">The service used to inspect files and directories.</param>
/// <param name="systemStorage">The service used to query drive readiness and free space.</param>
/// <param name="passwordService">The service used to assess password strength for warnings.</param>
internal sealed class BackupRequestValidator(
    IFileOperationsService fileOperations,
    ISystemStorageService systemStorage,
    IPasswordService passwordService
) : IBackupRequestValidator
{
    /// <summary>
    /// The comparison applied to backup paths: case-insensitive on Windows and case-sensitive
    /// elsewhere, matching how each platform's file system distinguishes names.
    /// </summary>
    /// <remarks>
    /// This mirrors the comparison the chunked backup service applies. Comparing case-insensitively on
    /// Unix would reject <c>/data/Backup</c> alongside <c>/data/backup</c> as the same directory, even
    /// though they are two distinct directories there.
    /// </remarks>
    private static readonly StringComparison PathComparer = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    /// <summary>
    /// Analyzes a request for blocking errors such as invalid or missing paths, password problems,
    /// and source/destination overlap.
    /// </summary>
    /// <remarks>
    /// The source and destination overlap checks are best-effort: when the paths cannot be probed they
    /// are skipped, so an unreadable path never blocks the backup with a spurious error.
    /// </remarks>
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
                    if (string.Equals(sourcePath, destinationPath, PathComparer))
                    {
                        errors.Add(
                            new LocalizableMessage(MessageCode.SourceDestinationSameDirectory)
                        );
                    }
                    else if (
                        destinationPath.StartsWith(
                            sourcePath + Path.DirectorySeparatorChar,
                            PathComparer
                        )
                    )
                    {
                        errors.Add(new LocalizableMessage(MessageCode.DestinationInsideSource));
                    }
                    else if (
                        sourcePath.StartsWith(
                            destinationPath + Path.DirectorySeparatorChar,
                            PathComparer
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
    /// Analyzes a request for advisory warnings: low free space at the destination, files already
    /// present in the destination for a create or restore, and a weak password on a create.
    /// </summary>
    /// <remarks>
    /// Every probe is advisory and never fails the operation: a file whose size cannot be read counts
    /// as zero bytes toward the free-space estimate, and a failure part-way through returns the
    /// warnings gathered so far.
    /// </remarks>
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
            }

            var existingFileCount = 0;

            if (fileOperations.DirectoryExists(destinationPath))
            {
                var existingFiles = await fileOperations.GetFilesAsync(
                    destinationPath,
                    "*",
                    cancellationToken
                );
                existingFileCount = existingFiles.Length;
            }

            if (
                existingFileCount > 0
                && request.Operation is BackupOperation.Create or BackupOperation.Restore
            )
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
