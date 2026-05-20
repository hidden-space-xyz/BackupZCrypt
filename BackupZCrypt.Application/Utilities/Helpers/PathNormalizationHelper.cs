using BackupZCrypt.Application.Resources;

namespace BackupZCrypt.Application.Utilities.Helpers;

internal static class PathNormalizationHelper
{
    internal static string? TryNormalize(string rawPath, out string? error)
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
            error = string.Format(Messages.InvalidPathFormat, ex.Message);
            return null;
        }
    }
}
