namespace BackupZCrypt.Domain.Strategies.Interfaces;

/// <summary>
/// Splits a source stream into variable-sized chunks for independent processing.
/// </summary>
public interface IChunkingStrategy
{
    /// <summary>
    /// Reads the source stream and yields its content as a sequence of chunks.
    /// </summary>
    /// <param name="source">The stream to split into chunks.</param>
    /// <param name="cancellationToken">Token used to cancel the chunking enumeration.</param>
    /// <returns>An asynchronous sequence of chunk buffers in stream order.</returns>
    IAsyncEnumerable<ReadOnlyMemory<byte>> ChunkAsync(
        Stream source,
        CancellationToken cancellationToken = default
    );
}
