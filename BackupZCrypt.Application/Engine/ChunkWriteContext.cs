using System.Collections.Concurrent;

namespace BackupZCrypt.Application.Engine;

/// <summary>
/// The resources one create or update shares between its files: the slots bounding how many chunks
/// are processed at once, and the record of the chunk files the run wrote.
/// </summary>
/// <param name="maximumChunksInFlight">The number of chunks processed at once.</param>
internal sealed class ChunkWriteContext(int maximumChunksInFlight) : IDisposable
{
    /// <summary>
    /// Gets the slots a chunk takes while it is compressed, encrypted, and written.
    /// </summary>
    public SemaphoreSlim ChunkSlots { get; } = new(maximumChunksInFlight, maximumChunksInFlight);

    /// <summary>
    /// Gets the chunk files this run created.
    /// </summary>
    public ConcurrentBag<string> WrittenChunks { get; } = [];

    /// <summary>
    /// Releases the chunk slots.
    /// </summary>
    public void Dispose()
    {
        this.ChunkSlots.Dispose();
    }
}
