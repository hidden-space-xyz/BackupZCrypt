using System.Buffers;
using System.Text;

using BackupZCrypt.Domain.Services.Interfaces;

namespace BackupZCrypt.Application.Utilities.Helpers;

/// <summary>
/// The rules governing how a file path is written into a manifest and read back out of one.
/// </summary>
/// <remarks>
/// <para>
/// A manifest entry path is portable data, not a host path. It is recorded in one canonical form,
/// with <c>/</c> separators and Unicode normalization form C, so an archive written on Windows
/// rebuilds the same tree on Unix. A path in any other form is rejected on every platform, which is
/// also what keeps traversal detection platform-independent: a crafted <c>..\..\escape</c> is refused
/// on Unix, where <c>\</c> is otherwise a legal file-name character, exactly as it is on Windows.
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
    /// The separator between the segments of a manifest entry path.
    /// </summary>
    private const char ManifestPathSeparator = '/';

    /// <summary>
    /// The characters Windows refuses inside a file or folder name.
    /// </summary>
    private static readonly SearchValues<char> WindowsInvalidFileNameChars =
        SearchValues.Create(['<', '>', ':', '"', '|', '?', '*', '\\']);

    /// <summary>
    /// Device names Windows resolves specially even when they carry an extension.
    /// </summary>
    private static readonly HashSet<string> ReservedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// Validates the structure of a manifest entry path: it is relative, in canonical form, free of
    /// traversal, current-directory, and empty segments, and holds no NUL character.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Applied to paths both on the way into and on the way out of a manifest, so neither a hostile
    /// source tree nor a crafted manifest can steer a write outside the destination.
    /// </para>
    /// <para>
    /// The rule is the same on every host. Names that are legal on one system and not on another —
    /// a colon or a trailing dot on Linux, for example — are a property of the restore target, not of
    /// the archive, so they are checked per file by <see cref="IsValidOnThisSystem"/> when restoring
    /// instead of rejecting the whole manifest.
    /// </para>
    /// </remarks>
    /// <param name="relativePath">The entry path to validate.</param>
    /// <exception cref="InvalidDataException">
    /// The path is empty, rooted, not in canonical form, contains a NUL character, or has an empty or
    /// relative segment.
    /// </exception>
    internal static void ValidateRelative(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new InvalidDataException("Manifest entry path is empty.");
        }

        if (relativePath[0] is ManifestPathSeparator)
        {
            throw new InvalidDataException("Manifest entry path must be relative.");
        }

        if (relativePath.Contains('\0', StringComparison.Ordinal))
        {
            throw new InvalidDataException("Manifest entry path contains invalid characters.");
        }

        if (relativePath.Contains('\\', StringComparison.Ordinal) || !IsComposed(relativePath))
        {
            throw new InvalidDataException("Manifest entry path is not in canonical form.");
        }

        var pathSegments = relativePath.Split(ManifestPathSeparator);

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
    }

    /// <summary>
    /// Determines whether every segment of a structurally valid manifest path can be created as a
    /// file or folder name on the running system.
    /// </summary>
    /// <param name="relativePath">The entry path taken from the manifest.</param>
    /// <returns><see langword="true"/> when the path can be restored here.</returns>
    internal static bool IsValidOnThisSystem(string relativePath)
    {
        return !OperatingSystem.IsWindows() || IsValidWindowsPath(relativePath);
    }

    /// <summary>
    /// Determines whether every segment of a manifest path is a name Windows accepts: free of the
    /// reserved characters and of control characters, not ending in a dot or a space, and not a
    /// device name such as <c>CON</c> or <c>aux.c</c>.
    /// </summary>
    /// <param name="relativePath">The entry path taken from the manifest.</param>
    /// <returns><see langword="true"/> when Windows can create the path.</returns>
    internal static bool IsValidWindowsPath(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        return !relativePath.Split(ManifestPathSeparator).Any(IsInvalidWindowsSegment);
    }

    /// <summary>
    /// Determines whether a path segment is a name Windows refuses or resolves to a device.
    /// </summary>
    /// <param name="segment">The individual manifest path segment.</param>
    /// <returns><see langword="true"/> when Windows cannot create the segment as a regular name.</returns>
    private static bool IsInvalidWindowsSegment(string segment)
    {
        var deviceName = segment.Split('.', 2)[0].TrimEnd(' ');
        return segment.AsSpan().IndexOfAny(WindowsInvalidFileNameChars) >= 0
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
    /// inside it. A name the running system cannot create is rejected before it is resolved, which on
    /// Windows also keeps a colon from being read as a drive or an alternate data stream.
    /// </remarks>
    /// <param name="destinationRoot">The directory restored files must stay within.</param>
    /// <param name="relativePath">The entry path taken from the manifest.</param>
    /// <returns>The absolute path the restored file may be written to.</returns>
    /// <exception cref="InvalidDataException">The path is invalid or resolves outside the destination root.</exception>
    internal static string ResolveSafeDestination(string destinationRoot, string relativePath)
    {
        ValidateRelative(relativePath);

        if (!IsValidOnThisSystem(relativePath))
        {
            throw new InvalidDataException("Manifest entry path is not a valid name on this system.");
        }

        var rootFullPath = Path.GetFullPath(destinationRoot);
        var destinationFullPath = Path.GetFullPath(
            Path.Join(rootFullPath, ToPlatformPath(relativePath))
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
    /// <param name="manifestPath">The entry path taken from the manifest.</param>
    /// <returns>The path with every separator replaced by the platform's directory separator.</returns>
    internal static string ToPlatformPath(string manifestPath)
    {
        return manifestPath.Replace(ManifestPathSeparator, Path.DirectorySeparatorChar);
    }

    /// <summary>
    /// Determines whether a path is in Unicode normalization form C.
    /// </summary>
    /// <param name="path">The path to inspect.</param>
    /// <returns><see langword="false"/> for a path in another form or that is not valid Unicode.</returns>
    private static bool IsComposed(string path)
    {
        try
        {
            return path.IsNormalized(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            return false;
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
