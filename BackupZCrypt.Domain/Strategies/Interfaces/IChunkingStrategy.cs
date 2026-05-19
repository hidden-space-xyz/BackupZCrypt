namespace BackupZCrypt.Domain.Strategies.Interfaces;

public interface IChunkingStrategy
{
    IAsyncEnumerable<ReadOnlyMemory<byte>> ChunkAsync(
        Stream source,
        CancellationToken cancellationToken = default);
}
