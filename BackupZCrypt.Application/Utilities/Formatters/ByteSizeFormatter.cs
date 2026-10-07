using System.Globalization;

namespace BackupZCrypt.Application.Utilities.Formatters;

/// <summary>
/// Formats raw byte counts into human-readable strings using binary (1024-based) units.
/// </summary>
public static class ByteSizeFormatter
{
    /// <summary>
    /// The unit suffixes in ascending order, each one 1024 times the previous; the last entry caps the
    /// scaling, so sizes beyond a terabyte keep growing in TB.
    /// </summary>
    private static readonly string[] Suffixes = ["B", "KB", "MB", "GB", "TB"];

    /// <summary>
    /// Formats a byte count as a string scaled to the largest fitting unit (for example, "1.5 MB").
    /// </summary>
    /// <param name="bytes">The number of bytes to format; the magnitude is used for negative values.</param>
    /// <returns>
    /// A formatted size string with a unit suffix: whole bytes below one kilobyte, and one decimal
    /// place for every larger unit.
    /// </returns>
    public static string Format(long bytes)
    {
        double size = Math.Abs((double)bytes);
        var suffixIndex = 0;
        while (size >= 1024 && suffixIndex < Suffixes.Length - 1)
        {
            size /= 1024;
            suffixIndex++;
        }

        return suffixIndex is 0
            ? string.Create(CultureInfo.CurrentCulture, $"{size:F0} {Suffixes[0]}")
            : string.Create(CultureInfo.CurrentCulture, $"{size:F1} {Suffixes[suffixIndex]}");
    }
}
