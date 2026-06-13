using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Application.Utilities.Helpers;

/// <summary>
/// Normalizes user-supplied paths by expanding environment variables and resolving them to absolute form.
/// </summary>
internal static class PathNormalizationHelper
{
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
