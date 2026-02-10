using CloudZCrypt.Domain.Strategies.Interfaces;
using K4os.Compression.LZ4.Streams;

namespace CloudZCrypt.Infrastructure.Strategies.Compression;

internal class LZ4CompressionStrategy : ICompressionStrategy
{
    public Domain.Enums.CompressionMode Id => Domain.Enums.CompressionMode.LZ4;

    public string DisplayName => "LZ4";

    public string Description =>
        "An extremely fast lossless compression algorithm focused on compression and decompression speed. "
        + "Delivers the highest throughput among modern compressors at the cost of a slightly lower compression ratio. "
        + "Widely used in file systems (ZFS, Btrfs), databases, and real-time applications.";

    public string Summary => "Fastest compression and decompression";

    public async Task<Stream> CompressAsync(
        Stream inputStream,
        CancellationToken cancellationToken = default
    )
    {
        MemoryStream output = new();
        using (LZ4EncoderStream lz4 = LZ4Stream.Encode(output, leaveOpen: true))
        {
            await inputStream.CopyToAsync(lz4, cancellationToken);
        }
        output.Position = 0;
        return output;
    }

    public async Task<Stream> DecompressAsync(
        Stream inputStream,
        CancellationToken cancellationToken = default
    )
    {
        MemoryStream output = new();
        using (LZ4DecoderStream lz4 = LZ4Stream.Decode(inputStream, leaveOpen: true))
        {
            await lz4.CopyToAsync(output, cancellationToken);
        }
        output.Position = 0;
        return output;
    }
}
