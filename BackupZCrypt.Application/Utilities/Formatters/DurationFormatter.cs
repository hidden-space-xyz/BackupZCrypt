using System.Globalization;

namespace BackupZCrypt.Application.Utilities.Formatters;

/// <summary>
/// Formats a <see cref="TimeSpan"/> into a compact, human-readable string using language-neutral
/// unit symbols (<c>d</c>, <c>h</c>, <c>min</c>, <c>s</c>). At most the two most significant units
/// are shown so estimates stay readable across the full range from sub-second to multi-year
/// durations.
/// </summary>
public static class DurationFormatter
{
    /// <summary>
    /// Formats a non-negative duration, choosing the granularity that best fits its magnitude
    /// (for example, "3 min 20 s", "2 h 5 min", or "0.4 s").
    /// </summary>
    /// <param name="duration">The duration to format; must not be negative.</param>
    /// <returns>A compact, human-readable representation of <paramref name="duration"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="duration"/> is negative.</exception>
    public static string Format(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);

        return duration switch
        {
            { TotalSeconds: < 1 } => string.Create(
                CultureInfo.CurrentCulture,
                $"{duration.TotalSeconds:0.0} s"
            ),
            { TotalMinutes: < 1 } => string.Create(
                CultureInfo.CurrentCulture,
                $"{(int)duration.TotalSeconds} s"
            ),
            { TotalHours: < 1 } => string.Create(
                CultureInfo.CurrentCulture,
                $"{(int)duration.TotalMinutes} min {duration.Seconds} s"
            ),
            { TotalDays: < 1 } => string.Create(
                CultureInfo.CurrentCulture,
                $"{(int)duration.TotalHours} h {duration.Minutes} min"
            ),
            _ => string.Create(
                CultureInfo.CurrentCulture,
                $"{(int)duration.TotalDays} d {duration.Hours} h"
            ),
        };
    }
}
