using CloudZCrypt.Domain.Strategies.Interfaces;
using SharpCompress.Compressors;
using SharpCompress.Compressors.BZip2;

namespace CloudZCrypt.Infrastructure.Strategies.Compression;

internal class BZip2CompressionStrategy : ICompressionStrategy
{
    public Domain.Enums.CompressionMode Id => Domain.Enums.CompressionMode.BZip2;

    public string DisplayName => "BZip2";

    public string Description =>
        "A high-quality block-sorting compression algorithm that achieves better compression ratios "
        + "than GZip at the cost of slower speed. Well suited for archiving scenarios where file size "
        + "reduction is more important than throughput.";

    public string Summary => "Higher compression ratio than GZip";

    public async Task<Stream> CompressAsync(
        Stream inputStream,
        CancellationToken cancellationToken = default
    )
    {
        MemoryStream output = new();
        using (BZip2Stream bzip2 = new(output, CompressionMode.Compress, false))
        {
            await inputStream.CopyToAsync(bzip2, cancellationToken);
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
        using (BZip2Stream bzip2 = new(inputStream, CompressionMode.Decompress, false))
        {
            await bzip2.CopyToAsync(output, cancellationToken);
        }
        output.Position = 0;
        return output;
    }
}
