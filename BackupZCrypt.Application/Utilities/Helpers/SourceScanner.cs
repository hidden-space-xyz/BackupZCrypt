using BackupZCrypt.Domain.Services.Interfaces;
using BackupZCrypt.Domain.ValueObjects.FileSystem;
using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Application.Utilities.Helpers;

/// <summary>
/// Walks a source folder once and turns what it finds into the manifest-ready view a create or
/// update works from: every regular file with its canonical manifest path, the empty folders to
/// recreate, and every entry that cannot be backed up together with the reason, so nothing the user
/// selected is left out silently.
/// </summary>
internal static class SourceScanner
{
    /// <summary>
    /// Walks the source folder and classifies everything below it.
    /// </summary>
    /// <param name="fileOperationsService">The service used to walk the folder.</param>
    /// <param name="sourcePath">The normalized source folder.</param>
    /// <param name="cancellationToken">A token to cancel the walk.</param>
    /// <returns>The snapshot, or <see langword="null"/> when the folder does not exist.</returns>
    /// <exception cref="UnauthorizedAccessException">The source folder itself cannot be listed.</exception>
    internal static async Task<SourceSnapshot?> ScanAsync(
        IFileOperationsService fileOperationsService,
        string sourcePath,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(fileOperationsService);

        DirectoryScan scan;
        try
        {
            scan = await fileOperationsService
                .ScanDirectoryAsync(sourcePath, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }

        List<LocalizableMessage> errors = [];

        List<string> inaccessibleDirectories = [];
        foreach (var directory in scan.InaccessibleDirectories.OrderBy(static d => d.Path, StringComparer.Ordinal))
        {
            var relative = DisplayPath(fileOperationsService, sourcePath, directory.Path);
            inaccessibleDirectories.Add(relative);
            errors.Add(
                new LocalizableMessage(
                    MessageCode.InaccessibleFolderFormat,
                    relative,
                    FailureReason.From(directory.Error)
                )
            );
        }

        List<SourceFile> candidates = [];
        List<string> unsupportedNames = [];

        foreach (var file in scan.Files)
        {
            var display = DisplayPath(fileOperationsService, sourcePath, file);

            if (TryGetManifestPath(fileOperationsService, sourcePath, file, out var manifestPath))
            {
                candidates.Add(new SourceFile(file, manifestPath, display));
            }
            else
            {
                unsupportedNames.Add(display);
            }
        }

        unsupportedNames.Sort(StringComparer.Ordinal);
        errors.AddRange(
            unsupportedNames.Select(static name => new LocalizableMessage(
                MessageCode.FileBackupErrorFormat,
                name,
                new LocalizableMessage(MessageCode.ReasonNameNotSupported)
            ))
        );

        candidates.Sort(
            static (left, right) =>
            {
                var byManifestPath = string.CompareOrdinal(left.RelativePath, right.RelativePath);
                return byManifestPath is not 0
                    ? byManifestPath
                    : string.CompareOrdinal(left.DisplayPath, right.DisplayPath);
            }
        );

        List<SourceFile> files = [];
        List<NameCollision> collisions = [];

        foreach (var candidate in candidates)
        {
            if (
                files.Count > 0
                && string.Equals(files[^1].RelativePath, candidate.RelativePath, StringComparison.Ordinal)
            )
            {
                collisions.Add(new NameCollision(candidate.DisplayPath, files[^1].DisplayPath));
                errors.Add(
                    new LocalizableMessage(
                        MessageCode.NameCollisionFormat,
                        candidate.DisplayPath,
                        files[^1].DisplayPath
                    )
                );
                continue;
            }

            files.Add(candidate);
        }

        List<string> emptyDirectories = [];
        foreach (var directory in scan.EmptyDirectories)
        {
            if (TryGetManifestPath(fileOperationsService, sourcePath, directory, out var manifestPath))
            {
                emptyDirectories.Add(manifestPath);
            }
        }

        emptyDirectories.Sort(StringComparer.Ordinal);

        List<string> skippedLinks =
        [
            .. scan
                .SkippedLinks.Select(link => DisplayPath(fileOperationsService, sourcePath, link))
                .Order(StringComparer.Ordinal),
        ];

        return new SourceSnapshot(
            files,
            emptyDirectories.Distinct(StringComparer.Ordinal).ToList(),
            inaccessibleDirectories,
            skippedLinks,
            collisions,
            unsupportedNames,
            errors
        );
    }

    /// <summary>
    /// Converts a path under the source into its canonical manifest form and checks its structure.
    /// </summary>
    /// <param name="fileOperationsService">The service used to compute the relative path.</param>
    /// <param name="sourcePath">The source folder.</param>
    /// <param name="fullPath">The entry below the source folder.</param>
    /// <param name="manifestPath">The canonical manifest path when the name can be recorded.</param>
    /// <returns><see langword="true"/> when the entry can be recorded in a manifest.</returns>
    private static bool TryGetManifestPath(
        IFileOperationsService fileOperationsService,
        string sourcePath,
        string fullPath,
        out string manifestPath
    )
    {
        try
        {
            manifestPath = ManifestPathPolicy.ToManifestPath(
                fileOperationsService.GetRelativePath(sourcePath, fullPath)
            );
            ManifestPathPolicy.ValidateRelative(manifestPath);
            return true;
        }
        catch (InvalidDataException)
        {
            manifestPath = string.Empty;
            return false;
        }
    }

    /// <summary>
    /// Expresses an entry below the source relative to it, as the user reads it in messages.
    /// </summary>
    /// <param name="fileOperationsService">The service used to compute the relative path.</param>
    /// <param name="sourcePath">The source folder.</param>
    /// <param name="fullPath">The entry below the source folder.</param>
    /// <returns>The relative path, or the full path when it cannot be made relative.</returns>
    private static string DisplayPath(
        IFileOperationsService fileOperationsService,
        string sourcePath,
        string fullPath
    )
    {
        try
        {
            return fileOperationsService.GetRelativePath(sourcePath, fullPath);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException)
        {
            return fullPath;
        }
    }
}

/// <summary>
/// One regular file below the source folder that can be recorded in a manifest.
/// </summary>
/// <param name="FullPath">The file's full path.</param>
/// <param name="RelativePath">The canonical manifest path.</param>
/// <param name="DisplayPath">The path relative to the source, as the user reads it.</param>
internal sealed record class SourceFile(string FullPath, string RelativePath, string DisplayPath);

/// <summary>
/// A file left out because its name only differs from another file's in its Unicode form.
/// </summary>
/// <param name="SkippedPath">The path of the file left out.</param>
/// <param name="KeptPath">The path of the file kept under the shared name.</param>
internal sealed record class NameCollision(string SkippedPath, string KeptPath);

/// <summary>
/// What a create or update works from after walking the source folder.
/// </summary>
/// <param name="Files">The files to back up, ordered by canonical manifest path.</param>
/// <param name="EmptyDirectories">The canonical paths of the empty folders to recreate.</param>
/// <param name="InaccessibleDirectories">The relative paths of the folders that could not be read.</param>
/// <param name="SkippedLinks">The relative paths of the links that were not followed.</param>
/// <param name="Collisions">The files left out because of a name collision.</param>
/// <param name="UnsupportedNames">The relative paths of the files whose names cannot be recorded.</param>
/// <param name="Errors">The per-folder and per-file problems found while walking, ready to report.</param>
internal sealed record class SourceSnapshot(
    IReadOnlyList<SourceFile> Files,
    IReadOnlyList<string> EmptyDirectories,
    IReadOnlyList<string> InaccessibleDirectories,
    IReadOnlyList<string> SkippedLinks,
    IReadOnlyList<NameCollision> Collisions,
    IReadOnlyList<string> UnsupportedNames,
    IReadOnlyList<LocalizableMessage> Errors
)
{
    /// <summary>
    /// Gets the warning that reports the links left out, or <see langword="null"/> when there are none.
    /// </summary>
    public LocalizableMessage? LinksWarning =>
        this.SkippedLinks.Count is 0
            ? null
            : new LocalizableMessage(
                MessageCode.SourceLinksSkippedFormat,
                this.SkippedLinks.Count,
                PathSample.Join(this.SkippedLinks)
            );

    /// <summary>
    /// Determines whether a manifest path lies inside a folder the walk could not read, which means
    /// its absence from the source says nothing about whether it still exists.
    /// </summary>
    /// <param name="manifestPath">The canonical manifest path to test.</param>
    /// <returns><see langword="true"/> when the path is below an unreadable folder.</returns>
    public bool IsInsideInaccessibleDirectory(string manifestPath)
    {
        ArgumentNullException.ThrowIfNull(manifestPath);

        foreach (var directory in this.InaccessibleDirectories)
        {
            var prefix = ManifestPathPolicy.Canonicalize(directory) + "/";
            if (manifestPath.StartsWith(prefix, PathNormalizationHelper.PathComparer))
            {
                return true;
            }
        }

        return false;
    }
}
