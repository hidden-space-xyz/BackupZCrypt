using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Application.Utilities.Helpers;

internal static class PathNormalizationHelper
{
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
