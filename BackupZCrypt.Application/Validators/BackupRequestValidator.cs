using System.Globalization;

using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.Utilities.Extensions;
using BackupZCrypt.Application.Utilities.Formatters;
using BackupZCrypt.Application.Utilities.Helpers;
using BackupZCrypt.Application.Validators.Interfaces;
using BackupZCrypt.Domain.Constants;
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
    /// The conservative comparison applied to backup paths: case-insensitive on Windows and macOS,
    /// whose default volumes ignore case, and case-sensitive elsewhere.
    /// </summary>
    /// <remarks>
    /// Shared with the chunked backup service so validation and execution cannot disagree on whether
    /// two paths denote the same directory.
    /// </remarks>
    private static readonly StringComparison PathComparer = PathNormalizationHelper.PathComparer;

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

        await ValidateSourcePathAsync(sourcePath, errors, cancellationToken);
        ValidateDestinationDrive(destinationPath, errors);
        ValidatePassword(request, errors);
        ValidateConfirmPassword(request, errors);

        try
        {
            ValidatePathOverlap(sourcePath, destinationPath, errors);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return errors;
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
            await CheckFreeSpaceAsync(sourcePath, destinationPath, warnings, cancellationToken);
            await CheckExistingDestinationFilesAsync(
                request,
                destinationPath,
                warnings,
                cancellationToken
            );
            CheckPasswordStrength(request, warnings);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return warnings;
        }

        return warnings;
    }

    /// <summary>
    /// Reports the blocking problems of the source path: empty, a file rather than a directory,
    /// missing, unreadable, or holding no files at all.
    /// </summary>
    /// <param name="sourcePath">The normalized source path.</param>
    /// <param name="errors">The list the findings are appended to.</param>
    /// <param name="cancellationToken">A token to cancel the listing.</param>
    /// <returns>A task that completes once the source has been inspected.</returns>
    private async Task ValidateSourcePathAsync(
        string sourcePath,
        List<LocalizableMessage> errors,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            errors.Add(new LocalizableMessage(MessageCode.SourcePathEmpty));
            return;
        }

        if (fileOperations.FileExists(sourcePath))
        {
            errors.Add(new LocalizableMessage(MessageCode.SourceMustBeDirectory));
            return;
        }

        if (!fileOperations.DirectoryExists(sourcePath))
        {
            errors.Add(new LocalizableMessage(MessageCode.SourcePathNotExistFormat, sourcePath));
            return;
        }

        try
        {
            var files = await fileOperations.GetFilesAsync(sourcePath, "*", cancellationToken);
            if (files.Length is 0)
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

    /// <summary>
    /// Reports an empty destination path, or a destination whose drive cannot be reached.
    /// </summary>
    /// <remarks>
    /// A root that cannot be determined at all is not an error on its own; only a known root that
    /// reports itself as not ready is.
    /// </remarks>
    /// <param name="destinationPath">The normalized destination path.</param>
    /// <param name="errors">The list the findings are appended to.</param>
    private void ValidateDestinationDrive(string destinationPath, List<LocalizableMessage> errors)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            errors.Add(new LocalizableMessage(MessageCode.DestinationPathEmpty));
            return;
        }

        try
        {
            var drive = systemStorage.GetPathRoot(destinationPath);

            if (!string.IsNullOrEmpty(drive) && !systemStorage.IsDriveReady(drive))
            {
                errors.Add(
                    new LocalizableMessage(MessageCode.DestinationDriveNotAccessibleFormat, drive)
                );
            }
        }
        catch (Exception ex)
        {
            errors.Add(new LocalizableMessage(MessageCode.DestinationInvalidFormat, ex.Message));
        }
    }

    /// <summary>
    /// Reports a missing password, one outside the accepted length range, or one padded with spaces
    /// a user cannot see.
    /// </summary>
    /// <param name="request">The backup request carrying the password.</param>
    /// <param name="errors">The list the findings are appended to.</param>
    private static void ValidatePassword(BackupRequest request, List<LocalizableMessage> errors)
    {
        if (string.IsNullOrWhiteSpace(request.Password))
        {
            errors.Add(new LocalizableMessage(MessageCode.PasswordRequired));
            return;
        }

        if (request.Password.Length < PasswordConstants.MinLength)
        {
            errors.Add(new LocalizableMessage(MessageCode.PasswordTooShort));
        }

        if (request.Password.Length > PasswordConstants.MaxLength)
        {
            errors.Add(new LocalizableMessage(MessageCode.PasswordTooLong));
        }

        if (request.Password.Trim() != request.Password)
        {
            errors.Add(new LocalizableMessage(MessageCode.PasswordLeadingTrailingSpaces));
        }
    }

    /// <summary>
    /// Reports a missing or mismatched confirmation password, which only a create has to supply.
    /// </summary>
    /// <param name="request">The backup request carrying both password fields.</param>
    /// <param name="errors">The list the findings are appended to.</param>
    private static void ValidateConfirmPassword(
        BackupRequest request,
        List<LocalizableMessage> errors
    )
    {
        if (request.Operation is not BackupOperation.Create)
        {
            return;
        }

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

    /// <summary>
    /// Reports a destination that is the source itself, sits inside it, or contains it.
    /// </summary>
    /// <remarks>
    /// The probe is best-effort and the caller owns its failure handling: an exception raised here
    /// leaves the errors gathered so far untouched rather than blocking the backup.
    /// </remarks>
    /// <param name="sourcePath">The normalized source path.</param>
    /// <param name="destinationPath">The normalized destination path.</param>
    /// <param name="errors">The list the findings are appended to.</param>
    private void ValidatePathOverlap(
        string sourcePath,
        string destinationPath,
        List<LocalizableMessage> errors
    )
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(destinationPath))
        {
            return;
        }

        if (!fileOperations.DirectoryExists(sourcePath))
        {
            return;
        }

        if (string.Equals(sourcePath, destinationPath, PathComparer))
        {
            errors.Add(new LocalizableMessage(MessageCode.SourceDestinationSameDirectory));
        }
        else if (destinationPath.StartsWith(ContainmentPrefixOf(sourcePath), PathComparer))
        {
            errors.Add(new LocalizableMessage(MessageCode.DestinationInsideSource));
        }
        else if (sourcePath.StartsWith(ContainmentPrefixOf(destinationPath), PathComparer))
        {
            errors.Add(new LocalizableMessage(MessageCode.SourceInsideDestination));
        }
    }

    /// <summary>
    /// Returns the prefix every path contained by <paramref name="path"/> must start with: the path
    /// itself followed by exactly one directory separator.
    /// </summary>
    /// <param name="path">The normalized absolute path to build a containment prefix for.</param>
    /// <returns>The path terminated by exactly one directory separator.</returns>
    private static string ContainmentPrefixOf(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    /// <summary>
    /// Warns when the destination drive reports less free space than the source is estimated to need.
    /// </summary>
    /// <remarks>
    /// A file whose size cannot be read counts as zero bytes, and a negative free-space reading means
    /// the volume cannot be queried at all, so it is left alone rather than treated as full.
    /// </remarks>
    /// <param name="sourcePath">The normalized source path.</param>
    /// <param name="destinationPath">The normalized destination path.</param>
    /// <param name="warnings">The list the findings are appended to.</param>
    /// <param name="cancellationToken">A token to cancel the listing.</param>
    /// <returns>A task that completes once the estimate has been made.</returns>
    private async Task CheckFreeSpaceAsync(
        string sourcePath,
        string destinationPath,
        List<LocalizableMessage> warnings,
        CancellationToken cancellationToken
    )
    {
        if (!fileOperations.DirectoryExists(sourcePath))
        {
            return;
        }

        var sourceFiles = await fileOperations.GetFilesAsync(sourcePath, "*", cancellationToken);

        var destinationDrive = systemStorage.GetPathRoot(destinationPath);
        if (string.IsNullOrEmpty(destinationDrive) || !systemStorage.IsDriveReady(destinationDrive))
        {
            return;
        }

        var totalSize = sourceFiles.Sum(f =>
            fileOperations.TryGetFileSize(f, out var fileSize) ? fileSize : 0
        );

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

    /// <summary>
    /// Warns when a create or a restore is about to write into a destination that already holds files.
    /// </summary>
    /// <param name="request">The backup request whose operation decides whether the warning applies.</param>
    /// <param name="destinationPath">The normalized destination path.</param>
    /// <param name="warnings">The list the findings are appended to.</param>
    /// <param name="cancellationToken">A token to cancel the listing.</param>
    /// <returns>A task that completes once the destination has been listed.</returns>
    private async Task CheckExistingDestinationFilesAsync(
        BackupRequest request,
        string destinationPath,
        List<LocalizableMessage> warnings,
        CancellationToken cancellationToken
    )
    {
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
                    existingFileCount.ToString("N0", CultureInfo.CurrentCulture)
                )
            );
        }
    }

    /// <summary>
    /// Warns when a create is about to protect an archive with a weak password.
    /// </summary>
    /// <remarks>
    /// Only a create is checked: every other operation is opening an archive whose password was
    /// already chosen, so warning about it would be advice the user can no longer act on.
    /// </remarks>
    /// <param name="request">The backup request carrying the password.</param>
    /// <param name="warnings">The list the findings are appended to.</param>
    private void CheckPasswordStrength(BackupRequest request, List<LocalizableMessage> warnings)
    {
        if (request.Operation is not BackupOperation.Create)
        {
            return;
        }

        var analysis = passwordService.AnalyzePasswordStrength(request.Password);
        if (analysis.Strength < PasswordStrength.Good)
        {
            warnings.Add(new LocalizableMessage(MessageCode.WeakPasswordWarning));
        }
    }
}
