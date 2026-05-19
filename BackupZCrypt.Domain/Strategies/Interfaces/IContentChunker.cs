namespace BackupZCrypt.Domain.Strategies.Interfaces;

public interface IContentChunker
{
    IAsyncEnumerable<ReadOnlyMemory<byte>> ChunkAsync(
        Stream source,
        CancellationToken cancellationToken = default);
}
