namespace BackupZCrypt.Application.Utilities.Helpers;

/// <summary>
/// Builds the short list of example paths a warning shows when it is about many files at once.
/// </summary>
internal static class PathSample
{
    /// <summary>
    /// The number of paths named before the list is cut short.
    /// </summary>
    private const int MaximumNamedPaths = 5;

    /// <summary>
    /// Joins the first few paths, in order, and marks the list as incomplete when there are more.
    /// </summary>
    /// <param name="paths">The paths to sample.</param>
    /// <returns>The sampled paths joined by commas.</returns>
    internal static string Join(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var sample = paths.Take(MaximumNamedPaths + 1).ToList();

        return sample.Count > MaximumNamedPaths
            ? string.Join(", ", sample.Take(MaximumNamedPaths)) + ", …"
            : string.Join(", ", sample);
    }
}
