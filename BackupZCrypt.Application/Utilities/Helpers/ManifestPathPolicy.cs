using System.Buffers;
using System.Text;

using BackupZCrypt.Domain.Services.Interfaces;

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
    /// The Windows-reserved characters that may not appear within a portable path segment. Applying
    /// the same rule on every host prevents an archive created on Unix from becoming unrestorable or
    /// ambiguous on Windows.
    /// </summary>
    private static readonly SearchValues<char> PortableInvalidFileNameChars =
        SearchValues.Create(['<', '>', ':', '"', '|', '?', '*']);

    /// <summary>
    /// Device names Windows resolves specially even when they carry an extension.
    /// </summary>
    private static readonly HashSet<string> ReservedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// Validates that a manifest entry path is relative and free of traversal segments and illegal
    /// characters, including portable per-segment rules shared by every supported platform.
    /// </summary>
    /// <remarks>
    /// Applied to paths both on the way into and on the way out of a manifest, so neither a hostile
    /// source tree nor a crafted manifest can steer a write outside the destination.
    /// </remarks>
    /// <param name="relativePath">The entry path to validate.</param>
    /// <exception cref="InvalidDataException">
    /// The path is empty, rooted, contains invalid characters, or has an ambiguous segment.
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

        var pathSegments = relativePath.Split(ManifestPathSeparators, StringSplitOptions.None);

        if (
            pathSegments.Any(static segment =>
                string.IsNullOrEmpty(segment) || string.Equals(segment, "..", StringComparison.Ordinal)
            )
        )
        {
            throw new InvalidDataException("Manifest entry path contains traversal segments.");
        }

        if (pathSegments.Any(static segment => string.Equals(segment, ".", StringComparison.Ordinal)))
        {
            throw new InvalidDataException("Manifest entry path contains current-directory segments.");
        }

        if (pathSegments.Any(IsInvalidPortableSegment))
        {
            throw new InvalidDataException(
                "Manifest entry path contains a file name that is not portable."
            );
        }
    }

    /// <summary>
    /// Determines whether a path segment would be invalid or ambiguous on a supported host.
    /// </summary>
    /// <param name="segment">The individual manifest path segment.</param>
    /// <returns><see langword="true"/> when the segment cannot be represented portably.</returns>
    private static bool IsInvalidPortableSegment(string segment)
    {
        var deviceName = segment.Split('.', 2)[0].TrimEnd(' ');
        return segment.AsSpan().IndexOfAny(PortableInvalidFileNameChars) >= 0
            || segment.Any(static character => char.IsControl(character))
            || segment.EndsWith(' ')
            || segment.EndsWith('.')
            || ReservedFileNames.Contains(deviceName);
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
            current = Path.Combine(current, segment);
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
    /// Converts a host-relative path into the manifest's canonical, platform-independent form.
    /// </summary>
    /// <remarks>
    /// Forward slashes and Unicode normalization form C are canonical on disk, so archives written
    /// on different platforms use one representation and cannot contain visually equivalent paths
    /// that collide only when restored elsewhere.
    /// </remarks>
    /// <param name="relativePath">The path relative to the backup root, using host separators.</param>
    /// <returns>The path with canonical separators and Unicode composition.</returns>
    internal static string ToManifestPath(string relativePath)
    {
        if (Path.DirectorySeparatorChar is not '\\' && relativePath.Contains('\\'))
        {
            throw new InvalidDataException(
                "A source file name contains a backslash reserved by the manifest format."
            );
        }

        return Canonicalize(relativePath);
    }

    /// <summary>
    /// Normalizes separators and Unicode composition to the single representation used for path
    /// identity inside a manifest.
    /// </summary>
    /// <param name="manifestPath">The manifest path to canonicalize.</param>
    /// <returns>The path with forward slashes and Unicode normalization form C.</returns>
    internal static string Canonicalize(string manifestPath)
    {
        return manifestPath.Replace('\\', '/').Normalize(NormalizationForm.FormC);
    }

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
