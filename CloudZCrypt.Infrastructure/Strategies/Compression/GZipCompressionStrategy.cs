using CloudZCrypt.Domain.Strategies.Interfaces;
using CloudZCrypt.Infrastructure.Constants;
using CloudZCrypt.Infrastructure.Resources;
using CloudZCrypt.Infrastructure.Streams;
using SharpCompress.Compressors;
using SharpCompress.Compressors.Deflate;

namespace CloudZCrypt.Infrastructure.Strategies.Compression;

internal class GZipCompressionStrategy : ICompressionStrategy
{
    public Domain.Enums.CompressionMode Id => Domain.Enums.CompressionMode.GZip;

    public string DisplayName => Messages.GZipDisplayName;

    public string Description => Messages.GZipDescription;

    public string Summary => Messages.GZipSummary;

    public async Task<Stream> CompressAsync(
        Stream inputStream,
        CancellationToken cancellationToken = default
    )
    {
        MemoryStream output = new();
        using (
            GZipStream gzip = new(
                new NonClosingStreamWrapper(output),
                CompressionMode.Compress,
                CompressionLevel.Default
            )
        )
        {
            await inputStream.CopyToAsync(gzip, StreamConstants.CopyBufferSize, cancellationToken);
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
            await gzip.CopyToAsync(output, StreamConstants.CopyBufferSize, cancellationToken);
        }
        output.Position = 0;
        return output;
    }
}
