using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Strategies.Interfaces;
using ZstdSharp;

namespace BackupZCrypt.Infrastructure.Strategies.Compression;

/// <summary>
/// Base class for Zstandard (zstd) compression strategies via ZstdSharp. Derived types
/// select the compression level that maps to their <see cref="Domain.Enums.CompressionMode"/>.
/// </summary>
internal abstract class ZstdCompressionStrategyBase : ICompressionStrategy
{
    /// <summary>
    /// Gets the compression-mode identifier used to select the concrete strategy.
    /// </summary>
    public abstract Domain.Enums.CompressionMode Id { get; }

    /// <summary>
    /// Gets the zstd compression level applied by the concrete strategy. Higher values trade
    /// speed for a smaller output.
    /// </summary>
    protected abstract int CompressionLevel { get; }

    /// <summary>
    /// Compresses the input stream with zstd and returns a rewound stream positioned at the
    /// start of the compressed data. Compression is performed entirely in memory because chunk
    /// sizes are bounded by the chunking strategy; spilling plaintext-derived data to temporary
    /// files would leak unencrypted content to disk.
    /// </summary>
    /// <param name="inputStream">The data to compress.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A stream over the compressed data, positioned at offset zero.</returns>
    public async Task<Stream> CompressAsync(
        Stream inputStream,
        CancellationToken cancellationToken = default
    )
    {
        MemoryStream output = new();

        await using (CompressionStream zstd = new(output, CompressionLevel))
        {
            await inputStream.CopyToAsync(zstd, StreamConstants.CopyBufferSize, cancellationToken);
        }

        output.Position = 0;
        return output;
    }

    /// <summary>
    /// Wraps the compressed input stream in a zstd decompression stream. Decompression is
    /// streamed lazily, so the level does not need to match the one used for compression.
    /// </summary>
    /// <param name="inputStream">The compressed data to read from.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A stream that yields the decompressed data as it is read.</returns>
    public Task<Stream> DecompressAsync(
        Stream inputStream,
        CancellationToken cancellationToken = default
    )
    {
        return Task.FromResult<Stream>(new DecompressionStream(inputStream));
    }
}
