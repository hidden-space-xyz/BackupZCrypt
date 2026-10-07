namespace BackupZCrypt.Application.Utilities.Helpers;

/// <summary>
/// Decides how many files a chunked backup, update, restore, or verification processes at once.
/// Centralized here so the backup engine and the benchmark that estimates its duration run the same
/// number of file pipelines: a benchmark that ran more would multiply its measured throughput by
/// processors a real backup leaves idle and report a duration several times too short.
/// </summary>
internal static class FileParallelismPolicy
{
    /// <summary>
    /// The most file pipelines run at once on any machine. Each pipeline can hold several
    /// four-megabyte plaintext, compressed, and ciphertext buffers at once, so the cap bounds memory
    /// use instead of growing with the processor count.
    /// </summary>
    internal const int MaximumDegreeOfParallelism = 4;

    /// <summary>
    /// Returns how many file pipelines run at once on a machine with the given number of logical
    /// processors: one per processor, between one and <see cref="MaximumDegreeOfParallelism"/>.
    /// </summary>
    /// <param name="processorCount">The number of logical processors available to the process.</param>
    /// <returns>The number of files processed at once.</returns>
    internal static int ForProcessorCount(int processorCount)
    {
        return Math.Clamp(processorCount, 1, MaximumDegreeOfParallelism);
    }

    /// <summary>
    /// The most chunks compressed, encrypted, and written at once across every file of a run. Each
    /// holds up to three four-megabyte buffers, so the cap bounds memory to a few dozen megabytes.
    /// </summary>
    internal const int MaximumChunksInFlight = 8;

    /// <summary>
    /// Returns how many chunks a run processes at once on a machine with the given number of logical
    /// processors: one per processor, between two and <see cref="MaximumChunksInFlight"/>, so even a
    /// backup of a single large file keeps the processors busy.
    /// </summary>
    /// <param name="processorCount">The number of logical processors available to the process.</param>
    /// <returns>The number of chunks processed at once.</returns>
    internal static int ChunksInFlightForProcessorCount(int processorCount)
    {
        return Math.Clamp(processorCount, 2, MaximumChunksInFlight);
    }
}
