namespace BackupZCrypt.Application.Utilities.Helpers;

/// <summary>
/// The rules governing how a file path is written into a manifest and read back out of one.
/// </summary>
/// <remarks>
/// <para>
/// A manifest entry path is portable data, not a host path. It is always recorded with <c>/</c>
/// separators so an archive written on Windows rebuilds the same tree on Unix, and both separators
/// are recognized on every platform when reading — which is also what keeps traversal detection
/// platform-independent, since a crafted <c>..\..\escape</c> must be rejected on Unix, where
/// <c>\</c> is otherwise a legal file-name character.
/// </para>
/// <para>
/// This is a security boundary, not a formatting convenience: it is the check that stops a hostile
/// source tree or a crafted manifest from steering a restore write outside the destination
/// directory. It lives in one named, directly testable type rather than as private statics inside
/// the backup engine so that the rule can be read, and tested, on its own.
/// </para>
/// </remarks>
internal static class ManifestPathPolicy
{
    /// <summary>
    /// The characters that may never appear anywhere in a manifest entry path.
    /// </summary>
    private static readonly char[] InvalidPathChars = Path.GetInvalidPathChars();

    /// <summary>
    /// The separators recognized when splitting a manifest entry path into segments. Both are
    /// accepted on every platform so a path written on either notation is validated the same way.
    /// </summary>
    private static readonly char[] ManifestPathSeparators = ['/', '\\'];

    /// <summary>
    /// The characters that may not appear within a single path segment on Windows.
    /// </summary>
    private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();

    /// <summary>
    /// Validates that a manifest entry path is relative and free of traversal segments and illegal
    /// characters, with an extra per-segment file name check on Windows.
    /// </summary>
    /// <remarks>
    /// Applied to paths both on the way into and on the way out of a manifest, so neither a hostile
    /// source tree nor a crafted manifest can steer a write outside the destination.
    /// </remarks>
    /// <param name="relativePath">The entry path to validate.</param>
    /// <exception cref="InvalidDataException">
    /// The path is empty, rooted, contains invalid characters, or contains a <c>..</c> segment.
    /// </exception>
    internal static void ValidateRelative(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new InvalidDataException("Manifest entry path is empty.");
        }

        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException("Manifest entry path must be relative.");
        }

        if (relativePath.IndexOfAny(InvalidPathChars) >= 0)
        {
            throw new InvalidDataException("Manifest entry path contains invalid characters.");
        }

        var pathSegments = relativePath.Split(
            ManifestPathSeparators,
            StringSplitOptions.RemoveEmptyEntries
        );

        if (pathSegments.Any(static segment => segment == ".."))
        {
            throw new InvalidDataException("Manifest entry path contains traversal segments.");
        }

        if (
            OperatingSystem.IsWindows()
            && pathSegments.Any(static segment => segment.IndexOfAny(InvalidFileNameChars) >= 0)
        )
        {
            throw new InvalidDataException(
                "Manifest entry path contains invalid file name characters."
            );
        }
    }

    /// <summary>
    /// Resolves a manifest entry path against the restore root and confirms the result stays inside
    /// that root.
    /// </summary>
    /// <remarks>
    /// Both paths are fully resolved first and the root is compared with a trailing separator, so a
    /// sibling directory whose name merely starts with the root's name is not accepted as being
    /// inside it.
    /// </remarks>
    /// <param name="destinationRoot">The directory restored files must stay within.</param>
    /// <param name="relativePath">The entry path taken from the manifest.</param>
    /// <returns>The absolute path the restored file may be written to.</returns>
    /// <exception cref="InvalidDataException">The path is invalid or resolves outside the destination root.</exception>
    internal static string ResolveSafeDestination(string destinationRoot, string relativePath)
    {
        ValidateRelative(relativePath);

        var rootFullPath = Path.GetFullPath(destinationRoot);
        var destinationFullPath = Path.GetFullPath(
            Path.Combine(rootFullPath, ToPlatformPath(relativePath))
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
    /// Converts a host-relative path into the manifest's canonical, platform-independent form.
    /// </summary>
    /// <remarks>
    /// Forward slashes are the canonical separator on disk, the same convention archive formats use, so
    /// an archive records the same entry text no matter which platform wrote it.
    /// </remarks>
    /// <param name="relativePath">The path relative to the backup root, using host separators.</param>
    /// <returns>The path with every separator normalized to <c>/</c>.</returns>
    internal static string ToManifestPath(string relativePath) => relativePath.Replace('\\', '/');

    /// <summary>
    /// Converts a manifest entry path back into a path the running platform can resolve.
    /// </summary>
    /// <param name="manifestPath">The entry path taken from the manifest, in either notation.</param>
    /// <returns>The path with every separator replaced by the platform's directory separator.</returns>
    internal static string ToPlatformPath(string manifestPath)
    {
        return manifestPath
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
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
