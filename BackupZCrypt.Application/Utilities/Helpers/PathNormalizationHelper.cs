using System.Buffers;

using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Application.Utilities.Helpers;

/// <summary>
/// Normalizes user-supplied paths by expanding environment variables and resolving them to absolute form.
/// </summary>
internal static class PathNormalizationHelper
{
    /// <summary>
    /// The conservative comparison applied to backup paths: case-insensitive on Windows and macOS,
    /// whose default volumes are case-insensitive, and case-sensitive elsewhere. A case-sensitive
    /// volume may reject two otherwise distinct names rather than restore one over the other.
    /// </summary>
    /// <remarks>
    /// Every layer that decides whether two paths denote the same location must use this one value.
    /// Comparing case-insensitively on Unix would treat <c>/data/Backup</c> and <c>/data/backup</c>
    /// as the same directory when they are two distinct ones, and disagreeing on the rule between
    /// the validator and the backup engine would let a request pass validation and then be refused.
    /// </remarks>
    internal static readonly StringComparison PathComparer = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    /// <summary>
    /// The characters Windows refuses in a path once its root has been removed.
    /// </summary>
    private static readonly SearchValues<char> WindowsInvalidPathChars =
        SearchValues.Create(['<', '>', ':', '"', '|', '?', '*']);

    /// <summary>
    /// Attempts to expand and resolve a raw path to its absolute form.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A path wrapped in double quotes, as Windows Explorer's "Copy as path" produces, is unwrapped
    /// first. A path without a drive or root is rejected rather than resolved: it would otherwise be
    /// resolved against whatever directory the process happened to start in, which the user cannot
    /// see.
    /// </para>
    /// <para>
    /// On Windows, a path holding characters the file system refuses is rejected here with a message
    /// that names the problem, instead of failing later with the system's own description.
    /// </para>
    /// </remarks>
    /// <param name="rawPath">The raw path to normalize; whitespace yields an empty string.</param>
    /// <param name="error">When normalization fails, receives a localizable error describing why; otherwise <see langword="null"/>.</param>
    /// <returns>The normalized absolute path, an empty string for blank input, or <see langword="null"/> if normalization failed.</returns>
    internal static string? TryNormalize(string rawPath, out LocalizableMessage? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return string.Empty;
        }

        var entered = Unquote(rawPath.Trim());

        if (string.IsNullOrWhiteSpace(entered))
        {
            return string.Empty;
        }

        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(entered);

            if (!Path.IsPathFullyQualified(expanded))
            {
                error = new LocalizableMessage(MessageCode.PathMustBeAbsoluteFormat, entered);
                return null;
            }

            if (OperatingSystem.IsWindows() && HasInvalidWindowsCharacters(expanded))
            {
                error = new LocalizableMessage(MessageCode.PathInvalidCharactersFormat, entered);
                return null;
            }

            return Path.GetFullPath(expanded);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = new LocalizableMessage(MessageCode.PathInvalidCharactersFormat, entered);
            return null;
        }
    }

    /// <summary>
    /// Removes one pair of surrounding double quotes.
    /// </summary>
    /// <param name="path">The trimmed path.</param>
    /// <returns>The path without its surrounding quotes, trimmed again.</returns>
    private static string Unquote(string path)
    {
        return path.Length >= 2 && path[0] is '"' && path[^1] is '"'
            ? path[1..^1].Trim()
            : path;
    }

    /// <summary>
    /// Determines whether a fully qualified Windows path holds a character the file system refuses
    /// anywhere after its root.
    /// </summary>
    /// <param name="path">The fully qualified path.</param>
    /// <returns><see langword="true"/> when the path cannot exist on Windows.</returns>
    private static bool HasInvalidWindowsCharacters(string path)
    {
        var root = Path.GetPathRoot(path) ?? string.Empty;
        var remainder = path.AsSpan(root.Length);

        foreach (var character in remainder)
        {
            if (char.IsControl(character))
            {
                return true;
            }
        }

        return remainder.IndexOfAny(WindowsInvalidPathChars) >= 0;
    }
}
