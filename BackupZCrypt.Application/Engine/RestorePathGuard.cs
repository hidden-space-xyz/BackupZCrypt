using BackupZCrypt.Application.Common;
using BackupZCrypt.Domain.Archive;
using BackupZCrypt.Domain.Services.Interfaces;

namespace BackupZCrypt.Application.Engine;

/// <summary>
/// Keeps a restore inside the folder the user chose: it resolves each manifest entry path against
/// the restore root and refuses any write that would land outside it, directly or through a link.
/// </summary>
/// <remarks>
/// This is the host-side half of the security boundary whose archive-side half is
/// <see cref="ManifestPathPolicy"/>: that type decides which entry paths an archive may hold and
/// whether the running system can create their names, this one resolves an accepted path against
/// the restore root and refuses a target that would land outside it.
/// </remarks>
internal static class RestorePathGuard
{
    /// <summary>
    /// Resolves a manifest entry path against the restore root and confirms the result stays inside
    /// that root.
    /// </summary>
    /// <remarks>
    /// Both paths are fully resolved first and the root is compared with a trailing separator, so a
    /// sibling directory whose name merely starts with the root's name is not accepted as being
    /// inside it. A name the running system cannot create is rejected before it is resolved, which on
    /// Windows also keeps a colon from being read as a drive or an alternate data stream.
    /// </remarks>
    /// <param name="destinationRoot">The directory restored files must stay within.</param>
    /// <param name="relativePath">The entry path taken from the manifest.</param>
    /// <returns>The absolute path the restored file may be written to.</returns>
    /// <exception cref="InvalidDataException">The path is invalid or resolves outside the destination root.</exception>
    internal static string ResolveSafeDestination(string destinationRoot, string relativePath)
    {
        ManifestPathPolicy.ValidateRelative(relativePath);

        if (!ManifestPathPolicy.IsValidOnThisSystem(relativePath))
        {
            throw new InvalidDataException("Manifest entry path is not a valid name on this system.");
        }

        var rootFullPath = Path.GetFullPath(destinationRoot);
        var destinationFullPath = Path.GetFullPath(
            Path.Join(rootFullPath, ManifestPathPolicy.ToPlatformPath(relativePath))
        );
        var rootWithSeparator = EnsureTrailingDirectorySeparator(rootFullPath);

        return !destinationFullPath.StartsWith(
            rootWithSeparator,
            PathNormalizationHelper.PathComparer
        )
            ? throw new InvalidDataException("Manifest entry path escapes the restore directory.")
            : destinationFullPath;
    }

    /// <summary>
    /// Rejects an existing descendant directory reached through a symbolic link or junction.
    /// </summary>
    /// <remarks>
    /// Lexical containment alone cannot stop a path below the restore or archive root from resolving
    /// elsewhere through a link. Callers check before and after creating missing directories. The
    /// root itself is intentionally excluded because a user may explicitly choose a linked root.
    /// </remarks>
    /// <param name="fileOperationsService">The port used to inspect existing file-system entries.</param>
    /// <param name="rootPath">The trusted logical root.</param>
    /// <param name="descendantDirectory">The directory at or below the root to inspect.</param>
    /// <exception cref="InvalidDataException">
    /// The directory is outside the root or an existing descendant component is a reparse point.
    /// </exception>
    internal static void EnsureNoReparsePointDescendants(
        IFileOperationsService fileOperationsService,
        string rootPath,
        string descendantDirectory
    )
    {
        ArgumentNullException.ThrowIfNull(fileOperationsService);

        var rootFullPath = Path.GetFullPath(rootPath);
        var descendantFullPath = Path.GetFullPath(descendantDirectory);

        if (string.Equals(rootFullPath, descendantFullPath, PathNormalizationHelper.PathComparer))
        {
            return;
        }

        var rootWithSeparator = EnsureTrailingDirectorySeparator(rootFullPath);
        if (!descendantFullPath.StartsWith(rootWithSeparator, PathNormalizationHelper.PathComparer))
        {
            throw new InvalidDataException("Directory is outside the trusted root.");
        }

        var relative = Path.GetRelativePath(rootFullPath, descendantFullPath);
        var current = rootFullPath;

        foreach (
            var segment in relative.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries
            )
        )
        {
            current = Path.Join(current, segment);
            if (
                fileOperationsService.DirectoryExists(current)
                && fileOperationsService.IsReparsePoint(current)
            )
            {
                throw new InvalidDataException("Path traverses a symbolic link or junction.");
            }
        }
    }

    /// <summary>
    /// Appends a directory separator to a path unless it already ends with one.
    /// </summary>
    /// <param name="path">The path to normalize.</param>
    /// <returns>The path terminated by a directory separator.</returns>
    private static string EnsureTrailingDirectorySeparator(string path)
    {
        return
            path.EndsWith(Path.DirectorySeparatorChar)
            || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }
}
