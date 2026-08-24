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
    /// Attempts to expand and resolve a raw path to its absolute form.
    /// </summary>
    /// <param name="rawPath">The raw path to normalize; whitespace yields an empty string.</param>
    /// <param name="error">When normalization fails, receives a localizable error describing why; otherwise <see langword="null"/>.</param>
    /// <returns>The normalized absolute path, an empty string for blank input, or <see langword="null"/> if normalization failed.</returns>
    internal static string? TryNormalize(string rawPath, out LocalizableMessage? error)
    {
        error = null;
        try
        {
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                return string.Empty;
            }

            var expanded = Environment.ExpandEnvironmentVariables(rawPath.Trim());
            return Path.GetFullPath(expanded);
        }
        catch (Exception ex)
        {
            error = new LocalizableMessage(MessageCode.InvalidPathFormat, ex.Message);
            return null;
        }
    }
}
