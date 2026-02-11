using CloudZCrypt.Domain.Strategies.Interfaces;
using CloudZCrypt.Infrastructure.Streams;
using SharpCompress.Compressors;
using SharpCompress.Compressors.Deflate;

namespace CloudZCrypt.Infrastructure.Strategies.Compression;

internal class GZipCompressionStrategy : ICompressionStrategy
{
    public Domain.Enums.CompressionMode Id => Domain.Enums.CompressionMode.GZip;

    public string DisplayName => "GZip";

    public string Description =>
        "A widely supported compression algorithm (RFC 1952) based on the DEFLATE algorithm. "
        + "Offers a good balance between compression ratio and speed, and is universally supported "
        + "across operating systems, tools, and programming languages.";

    public string Summary => "Good balance, universal compatibility";

    public async Task<Stream> CompressAsync(
        Stream inputStream,
        CancellationToken cancellationToken = default
    )
    {
        MemoryStream output = new();
        using (GZipStream gzip = new(new NonClosingStreamWrapper(output), CompressionMode.Compress, CompressionLevel.Default))
        {
            await inputStream.CopyToAsync(gzip, cancellationToken);
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
        using (GZipStream gzip = new(inputStream, CompressionMode.Decompress))
        {
            await gzip.CopyToAsync(output, cancellationToken);
        }
        output.Position = 0;
        return output;
    }
}
