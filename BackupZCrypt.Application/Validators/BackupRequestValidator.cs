using System.Runtime.CompilerServices;

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
/// (invalid paths, missing sources, unusable passwords, a destination holding other data) and
/// advisory warnings (low disk space, weak passwords, entries that will be skipped, a backup that
/// will be replaced, files a restore may overwrite).
/// </summary>
/// <param name="fileOperations">The service used to inspect files and directories.</param>
/// <param name="systemStorage">The service used to query drive readiness, format, and free space.</param>
/// <param name="passwordService">The service used to assess password strength for warnings.</param>
internal sealed class BackupRequestValidator(
    IFileOperationsService fileOperations,
    ISystemStorageService systemStorage,
    IPasswordService passwordService
) : IBackupRequestValidator
{
    /// <summary>
    /// The average chunk size the content-defined chunker aims for, used to estimate chunk counts.
    /// </summary>
    private const long AverageChunkSize = 1024 * 1024;

    /// <summary>
    /// The number of chunk files above which a FAT32 destination risks running out of folder entries:
    /// each long chunk file name takes seven of the 65,534 entries FAT32 allows per folder.
    /// </summary>
    private const long FatChunkFileLimit = 8_000;

    /// <summary>
    /// The conservative comparison applied to backup paths: case-insensitive on Windows and macOS,
    /// whose default volumes ignore case, and case-sensitive elsewhere.
    /// </summary>
    private static readonly StringComparison PathComparer = PathNormalizationHelper.PathComparer;

    /// <summary>
    /// The source walk done for a request, kept for as long as the request object lives so the
    /// error and warning passes over the same request walk the tree once.
    /// </summary>
    private static readonly ConditionalWeakTable<BackupRequest, CachedScan> ScanCache = new();

    /// <summary>
    /// Analyzes a request for blocking errors such as invalid or missing paths, password problems,
    /// a destination that holds data other than a backup, and source/destination overlap.
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
        ArgumentNullException.ThrowIfNull(request);

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

        await this.ValidateSourcePathAsync(request, sourcePath, errors, cancellationToken);
        this.ValidateDestination(request, destinationPath, errors);
        ValidatePassword(request, errors);
        ValidateConfirmPassword(request, errors);
        ValidateAlgorithms(request, errors);

        try
        {
            this.ValidatePathOverlap(request.Operation, sourcePath, destinationPath, errors);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return errors;
        }

        return errors;
    }

    /// <summary>
    /// Analyzes a request for advisory warnings: low free space and a FAT32 destination for a create,
    /// a backup a create will replace, source entries a create or update will skip, files a restore
    /// may overwrite, and a weak password on a create.
    /// </summary>
    /// <remarks>
    /// Every probe is advisory and never fails the operation: a failure part-way through returns the
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
        ArgumentNullException.ThrowIfNull(request);

        List<LocalizableMessage> warnings = [];

        var sourcePath = PathNormalizationHelper.TryNormalize(request.SourcePath, out _);
        var destinationPath = PathNormalizationHelper.TryNormalize(request.DestinationPath, out _);
        if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(destinationPath))
        {
            return warnings;
        }

        try
        {
            switch (request.Operation)
            {
                case BackupOperation.Create:
                    var scan = await this.TryGetScanAsync(request, sourcePath, cancellationToken);
                    this.CheckReplacedBackup(destinationPath, warnings);

                    if (scan is not null)
                    {
                        AddSourceWarnings(scan, warnings);
                        this.CheckFreeSpace(scan, destinationPath, warnings);
                        this.CheckFatDestination(scan, destinationPath, warnings);
                    }

                    this.CheckPasswordStrength(request, warnings);
                    break;

                case BackupOperation.Update:
                    if (await this.TryGetScanAsync(request, sourcePath, cancellationToken) is { } updateScan)
                    {
                        AddSourceWarnings(updateScan, warnings);
                    }

                    break;

                case BackupOperation.Restore:
                    await this.CheckExistingDestinationFilesAsync(
                        destinationPath,
                        warnings,
                        cancellationToken
                    );
                    break;

                case BackupOperation.Verify:
                default:
                    break;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return warnings;
        }

        return warnings;
    }

    /// <summary>
    /// Reports the blocking problems of the source path: empty, a file rather than a directory,
    /// missing, unreadable, holding no file that can be backed up, or holding more files than one
    /// manifest can record.
    /// </summary>
    /// <param name="request">The request whose operation decides how deeply the source is inspected.</param>
    /// <param name="sourcePath">The normalized source path.</param>
    /// <param name="errors">The list the findings are appended to.</param>
    /// <param name="cancellationToken">A token to cancel the walk.</param>
    /// <returns>A task that completes once the source has been inspected.</returns>
    private async Task ValidateSourcePathAsync(
        BackupRequest request,
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
            if (request.Operation is not (BackupOperation.Create or BackupOperation.Update))
            {
                _ = fileOperations.GetDirectoryEntryNames(sourcePath);
                return;
            }

            var scan = await this.GetScanAsync(request, sourcePath, cancellationToken);

            if (scan.Snapshot.Files.Count is 0)
            {
                errors.Add(
                    new LocalizableMessage(
                        scan.Snapshot.SkippedLinks.Count > 0
                            ? MessageCode.SourceOnlyLinks
                            : MessageCode.SourceDirectoryEmpty
                    )
                );
                errors.AddRange(scan.Snapshot.Errors);
            }
        }
        catch (UnauthorizedAccessException)
        {
            errors.Add(new LocalizableMessage(MessageCode.SourceAccessDenied));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
        {
            errors.Add(new LocalizableMessage(MessageCode.SourceAccessErrorFormat, FailureReason.From(ex)));
        }
    }

    /// <summary>
    /// Reports an empty destination path, a destination whose drive cannot be reached, a destination
    /// that is a file, and — for a create — a destination folder that holds anything other than a
    /// backup, which a create must never overwrite.
    /// </summary>
    /// <remarks>
    /// A root that cannot be determined at all is not an error on its own; only a known root that
    /// reports itself as not ready is.
    /// </remarks>
    /// <param name="request">The request whose operation decides which rules apply.</param>
    /// <param name="destinationPath">The normalized destination path.</param>
    /// <param name="errors">The list the findings are appended to.</param>
    private void ValidateDestination(
        BackupRequest request,
        string destinationPath,
        List<LocalizableMessage> errors
    )
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
                return;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            errors.Add(new LocalizableMessage(MessageCode.DestinationInvalidFormat, FailureReason.From(ex)));
            return;
        }

        if (fileOperations.FileExists(destinationPath))
        {
            errors.Add(new LocalizableMessage(MessageCode.DestinationIsFile));
            return;
        }

        if (request.Operation is not BackupOperation.Create)
        {
            return;
        }

        try
        {
            if (
                fileOperations.DirectoryExists(destinationPath)
                && !BackupLayout.ContainsOnlyBackupFiles(fileOperations, destinationPath, out _)
            )
            {
                errors.Add(new LocalizableMessage(MessageCode.DestinationNotEmpty));
            }
        }
        catch (UnauthorizedAccessException)
        {
            errors.Add(new LocalizableMessage(MessageCode.DestinationAccessDenied));
        }
        catch (IOException)
        {
            return;
        }
    }

    /// <summary>
    /// Reports a missing password and, for a create, one outside the accepted length range or padded
    /// with spaces a user cannot see.
    /// </summary>
    /// <remarks>
    /// The length and spacing rules only apply when a password is chosen. Every other operation
    /// opens a backup whose password already exists, and refusing it here would lock the user out of
    /// a backup an earlier version allowed them to create.
    /// </remarks>
    /// <param name="request">The backup request carrying the password.</param>
    /// <param name="errors">The list the findings are appended to.</param>
    private static void ValidatePassword(BackupRequest request, List<LocalizableMessage> errors)
    {
        if (string.IsNullOrWhiteSpace(request.Password))
        {
            errors.Add(new LocalizableMessage(MessageCode.PasswordRequired));
            return;
        }

        if (request.Operation is not BackupOperation.Create)
        {
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
    /// Reports a create that names an algorithm or compression mode this version does not have, such
    /// as one read from a hand-edited settings file, before any key is derived.
    /// </summary>
    /// <param name="request">The backup request carrying the algorithm choices.</param>
    /// <param name="errors">The list the findings are appended to.</param>
    private static void ValidateAlgorithms(BackupRequest request, List<LocalizableMessage> errors)
    {
        if (
            request.Operation is BackupOperation.Create
            && (
                !Enum.IsDefined(request.EncryptionAlgorithm)
                || !Enum.IsDefined(request.KeyDerivationAlgorithm)
                || !Enum.IsDefined(request.Compression)
            )
        )
        {
            errors.Add(new LocalizableMessage(MessageCode.AlgorithmNotSupported));
        }
    }

    /// <summary>
    /// Reports a destination that is the source itself, sits inside it, or contains it.
    /// </summary>
    /// <remarks>
    /// A restore may write into a folder that contains the backup, such as the root of the drive the
    /// backup lives on: the restore itself refuses any file whose target falls inside the backup
    /// folder. The probe is best-effort and the caller owns its failure handling.
    /// </remarks>
    /// <param name="operation">The operation being validated.</param>
    /// <param name="sourcePath">The normalized source path.</param>
    /// <param name="destinationPath">The normalized destination path.</param>
    /// <param name="errors">The list the findings are appended to.</param>
    private void ValidatePathOverlap(
        BackupOperation operation,
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
        else if (
            operation is not BackupOperation.Restore
            && sourcePath.StartsWith(ContainmentPrefixOf(destinationPath), PathComparer)
        )
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
    /// Adds the warnings about source entries a create or update will leave out: links, unreadable
    /// folders, names that collide once normalized, and names a manifest cannot record.
    /// </summary>
    /// <param name="scan">The walked source.</param>
    /// <param name="warnings">The list the findings are appended to.</param>
    private static void AddSourceWarnings(CachedScan scan, List<LocalizableMessage> warnings)
    {
        var snapshot = scan.Snapshot;

        if (snapshot.LinksWarning is { } linksWarning)
        {
            warnings.Add(linksWarning);
        }

        if (snapshot.InaccessibleDirectories.Count > 0)
        {
            warnings.Add(
                new LocalizableMessage(
                    MessageCode.SourceInaccessibleFoldersFormat,
                    snapshot.InaccessibleDirectories.Count,
                    PathSample.Join(snapshot.InaccessibleDirectories)
                )
            );
        }

        if (snapshot.Collisions.Count > 0)
        {
            warnings.Add(
                new LocalizableMessage(
                    MessageCode.SourceNameCollisionsFormat,
                    snapshot.Collisions.Count,
                    PathSample.Join(snapshot.Collisions.Select(static c => c.SkippedPath))
                )
            );
        }

        if (snapshot.UnsupportedNames.Count > 0)
        {
            warnings.Add(
                new LocalizableMessage(
                    MessageCode.SourceUnsupportedNamesFormat,
                    snapshot.UnsupportedNames.Count,
                    PathSample.Join(snapshot.UnsupportedNames)
                )
            );
        }
    }

    /// <summary>
    /// Warns when a create is about to replace a backup already in the destination folder.
    /// </summary>
    /// <param name="destinationPath">The normalized destination path.</param>
    /// <param name="warnings">The list the findings are appended to.</param>
    private void CheckReplacedBackup(string destinationPath, List<LocalizableMessage> warnings)
    {
        try
        {
            if (
                fileOperations.DirectoryExists(destinationPath)
                && BackupLayout.ContainsOnlyBackupFiles(fileOperations, destinationPath, out var containsManifest)
                && containsManifest
            )
            {
                warnings.Add(new LocalizableMessage(MessageCode.DestinationContainsBackup));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }
    }

    /// <summary>
    /// Warns when the destination drive reports less free space than a create is estimated to need.
    /// </summary>
    /// <remarks>
    /// A negative free-space reading means the volume cannot be queried at all, so it is left alone
    /// rather than treated as full.
    /// </remarks>
    /// <param name="scan">The walked source.</param>
    /// <param name="destinationPath">The normalized destination path.</param>
    /// <param name="warnings">The list the findings are appended to.</param>
    private void CheckFreeSpace(CachedScan scan, string destinationPath, List<LocalizableMessage> warnings)
    {
        var destinationDrive = systemStorage.GetPathRoot(destinationPath);
        if (string.IsNullOrEmpty(destinationDrive) || !systemStorage.IsDriveReady(destinationDrive))
        {
            return;
        }

        var requiredSpace = (long)(scan.TotalBytes * 1.2);
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
    /// Warns when a large create targets a FAT32 drive, whose per-folder entry limit the chunks
    /// directory can exceed part-way through the backup.
    /// </summary>
    /// <param name="scan">The walked source.</param>
    /// <param name="destinationPath">The normalized destination path.</param>
    /// <param name="warnings">The list the findings are appended to.</param>
    private void CheckFatDestination(CachedScan scan, string destinationPath, List<LocalizableMessage> warnings)
    {
        var destinationDrive = systemStorage.GetPathRoot(destinationPath);
        if (string.IsNullOrEmpty(destinationDrive) || scan.EstimatedChunks <= FatChunkFileLimit)
        {
            return;
        }

        var format = systemStorage.GetDriveFormat(destinationDrive);
        if (format is not null && format.StartsWith("FAT", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add(new LocalizableMessage(MessageCode.FatFileSystemWarning));
        }
    }

    /// <summary>
    /// Warns when a restore is about to write into a destination that already holds files.
    /// </summary>
    /// <param name="destinationPath">The normalized destination path.</param>
    /// <param name="warnings">The list the findings are appended to.</param>
    /// <param name="cancellationToken">A token to cancel the listing.</param>
    /// <returns>A task that completes once the destination has been listed.</returns>
    private async Task CheckExistingDestinationFilesAsync(
        string destinationPath,
        List<LocalizableMessage> warnings,
        CancellationToken cancellationToken
    )
    {
        if (!fileOperations.DirectoryExists(destinationPath))
        {
            return;
        }

        var existingFiles = await fileOperations.GetFilesAsync(destinationPath, "*", cancellationToken);

        if (existingFiles.Length > 0)
        {
            warnings.Add(
                new LocalizableMessage(MessageCode.DestinationExistingFilesFormat, existingFiles.Length)
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

    /// <summary>
    /// Walks the source of a request for an advisory check, reporting a source that cannot be walked
    /// as absent instead of failing the whole warning sweep.
    /// </summary>
    /// <param name="request">The request whose source is walked.</param>
    /// <param name="sourcePath">The normalized source path.</param>
    /// <param name="cancellationToken">A token to cancel the walk.</param>
    /// <returns>The walked and measured source, or <see langword="null"/> when it cannot be walked.</returns>
    private async Task<CachedScan?> TryGetScanAsync(
        BackupRequest request,
        string sourcePath,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await this.GetScanAsync(request, sourcePath, cancellationToken);
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException and not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>
    /// Walks the source of a request once and measures it, reusing the walk for later passes over the
    /// same request.
    /// </summary>
    /// <param name="request">The request whose source is walked.</param>
    /// <param name="sourcePath">The normalized source path.</param>
    /// <param name="cancellationToken">A token to cancel the walk.</param>
    /// <returns>The walked and measured source.</returns>
    /// <exception cref="DirectoryNotFoundException">The source no longer exists.</exception>
    private async Task<CachedScan> GetScanAsync(
        BackupRequest request,
        string sourcePath,
        CancellationToken cancellationToken
    )
    {
        if (
            ScanCache.TryGetValue(request, out var cached)
            && string.Equals(cached.SourcePath, sourcePath, StringComparison.Ordinal)
        )
        {
            return cached;
        }

        var snapshot =
            await SourceScanner.ScanAsync(fileOperations, sourcePath, cancellationToken)
            ?? throw new DirectoryNotFoundException(sourcePath);

        long totalBytes = 0;
        long estimatedChunks = 0;

        foreach (var file in snapshot.Files)
        {
            var size = fileOperations.TryGetFileSize(file.FullPath, out var fileSize) ? fileSize : 0;

            totalBytes = size > long.MaxValue - totalBytes ? long.MaxValue : totalBytes + size;
            estimatedChunks += Math.Max(1, (size + AverageChunkSize - 1) / AverageChunkSize);
        }

        CachedScan scan = new(sourcePath, snapshot, totalBytes, estimatedChunks);
        ScanCache.AddOrUpdate(request, scan);
        return scan;
    }

    /// <summary>
    /// A walked and measured source.
    /// </summary>
    /// <param name="SourcePath">The normalized source path that was walked.</param>
    /// <param name="Snapshot">The walk's result.</param>
    /// <param name="TotalBytes">The total size of the files to back up.</param>
    /// <param name="EstimatedChunks">The estimated number of chunks the files split into.</param>
    private sealed record class CachedScan(
        string SourcePath,
        SourceSnapshot Snapshot,
        long TotalBytes,
        long EstimatedChunks
    );
}
