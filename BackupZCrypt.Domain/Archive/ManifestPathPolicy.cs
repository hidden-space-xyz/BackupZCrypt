using System.Buffers;
using System.Text;

namespace BackupZCrypt.Domain.Archive;

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
/// source tree or a crafted manifest from carrying a path that could steer a restore write outside
/// the destination directory. The restore then resolves every accepted path against the destination
/// and refuses one that still escapes it, which is the host-side half of the same boundary. Both live
/// in named, directly testable types rather than as private statics inside the backup engine so that
/// the rules can be read, and tested, on their own.
/// </para>
/// </remarks>
public static class ManifestPathPolicy
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
    public static void ValidateRelative(string relativePath)
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
    public static bool IsValidOnThisSystem(string relativePath)
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
    public static bool IsValidWindowsPath(string relativePath)
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
    /// Converts a host-relative path into the manifest's canonical, platform-independent form.
    /// </summary>
    /// <remarks>
    /// Forward slashes and Unicode normalization form C are canonical on disk, so archives written
    /// on different platforms use one representation and cannot contain visually equivalent paths
    /// that collide only when restored elsewhere.
    /// </remarks>
    /// <param name="relativePath">The path relative to the backup root, using host separators.</param>
    /// <returns>The path with canonical separators and Unicode composition.</returns>
    public static string ToManifestPath(string relativePath)
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
    public static string Canonicalize(string manifestPath)
    {
        return manifestPath.Replace('\\', '/').Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Converts a manifest entry path back into a path the running platform can resolve.
    /// </summary>
    /// <param name="manifestPath">The entry path taken from the manifest.</param>
    /// <returns>The path with every separator replaced by the platform's directory separator.</returns>
    public static string ToPlatformPath(string manifestPath)
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
}
