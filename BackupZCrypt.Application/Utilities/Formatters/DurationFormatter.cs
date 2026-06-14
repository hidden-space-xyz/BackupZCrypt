namespace BackupZCrypt.Application.Utilities.Formatters;

/// <summary>
/// Formats a <see cref="TimeSpan"/> into a compact, human-readable string using language-neutral
/// unit symbols (<c>d</c>, <c>h</c>, <c>min</c>, <c>s</c>). The two most significant units are shown
/// so estimates stay readable across the full range from sub-second to multi-year durations.
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

        if (duration.TotalSeconds < 1)
        {
            return $"{duration.TotalSeconds:0.0} s";
        }

        if (duration.TotalMinutes < 1)
        {
            return $"{(int)duration.TotalSeconds} s";
        }

        if (duration.TotalHours < 1)
        {
            return $"{(int)duration.TotalMinutes} min {duration.Seconds} s";
        }

        if (duration.TotalDays < 1)
        {
            return $"{(int)duration.TotalHours} h {duration.Minutes} min";
        }

        return $"{(int)duration.TotalDays} d {duration.Hours} h";
    }
}
