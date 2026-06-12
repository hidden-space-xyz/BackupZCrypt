using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Strategies.Interfaces;
using ZstdSharp;

namespace BackupZCrypt.Infrastructure.Strategies.Compression;

internal abstract class ZstdCompressionStrategyBase : ICompressionStrategy
{
    public abstract Domain.Enums.CompressionMode Id { get; }

    public abstract string DisplayName { get; }

    public abstract string Description { get; }

    public abstract string Summary { get; }

    protected abstract int CompressionLevel { get; }

    public async Task<Stream> CompressAsync(
        Stream inputStream,
        CancellationToken cancellationToken = default
    )
    {
        // Chunk sizes are bounded by the chunking strategy, so compression happens
        // fully in memory: spilling plaintext-derived data to temporary files would
        // leak unencrypted content to disk.
        MemoryStream output = new();

        await using (CompressionStream zstd = new(output, CompressionLevel))
        {
            await inputStream.CopyToAsync(zstd, StreamConstants.CopyBufferSize, cancellationToken);
        }

        output.Position = 0;
        return output;
    }

    public Task<Stream> DecompressAsync(
        Stream inputStream,
        CancellationToken cancellationToken = default
    )
    {
        return Task.FromResult<Stream>(new DecompressionStream(inputStream));
    }
}
