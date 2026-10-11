using BackupZCrypt.Domain.Archive;

namespace BackupZCrypt.Application.Engine;

/// <summary>
/// The bounds every create, update, restore, and verify run under: the largest chunk file the
/// reader accepts and how much work runs at once on this machine.
/// </summary>
internal static class EngineLimits
{
    /// <summary>
    /// The largest encrypted chunk file the reader accepts. Zstandard's bounded overhead is well
    /// below 64 KiB for a 4 MiB input; the margin rejects unbounded allocations without constraining
    /// any chunk the writer can produce.
    /// </summary>
    internal const int MaximumStoredChunkSize =
        BackupConstants.MaximumChunkSize + (64 * 1024) + EncryptionConstants.TagSize;

    /// <summary>
    /// The number of file pipelines run at once on this machine, as decided by
    /// <see cref="FileParallelismPolicy"/>.
    /// </summary>
    internal static readonly int MaximumParallelFileOperations =
        FileParallelismPolicy.ForProcessorCount(Environment.ProcessorCount);

    /// <summary>
    /// The number of chunks compressed, encrypted, and written at once across every file of a run.
    /// Bounding it caps memory at a few chunk buffers each, while still letting a single large file
    /// keep every processor busy instead of being processed one chunk at a time.
    /// </summary>
    internal static readonly int MaximumChunksInFlight =
        FileParallelismPolicy.ChunksInFlightForProcessorCount(Environment.ProcessorCount);
}
