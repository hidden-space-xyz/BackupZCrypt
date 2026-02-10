using CloudZCrypt.Domain.Strategies.Interfaces;
using ZstdSharp;

namespace CloudZCrypt.Infrastructure.Strategies.Compression;

internal class ZstandardCompressionStrategy : ICompressionStrategy
{
    private const int CompressionLevel = 3;

    public Domain.Enums.CompressionMode Id => Domain.Enums.CompressionMode.Zstandard;

    public string DisplayName => "Zstandard (Zstd)";

    public string Description =>
        "A modern, high-performance compression algorithm developed by Facebook (RFC 8478). "
        + "Delivers an excellent balance between compression speed and ratio, outperforming most traditional algorithms. "
        + "Widely adopted in Linux kernel, databases, and network protocols.";

    public string Summary => "Best balance of speed and compression";

    public async Task<Stream> CompressAsync(Stream inputStream, CancellationToken cancellationToken = default)
    {
        MemoryStream output = new();
        using (CompressionStream zstd = new(output, CompressionLevel, leaveOpen: true))
        {
            await inputStream.CopyToAsync(zstd, cancellationToken);
        }
        output.Position = 0;
        return output;
    }

    public async Task<Stream> DecompressAsync(Stream inputStream, CancellationToken cancellationToken = default)
    {
        MemoryStream output = new();
        using (DecompressionStream zstd = new(inputStream, leaveOpen: true))
        {
            await zstd.CopyToAsync(output, cancellationToken);
        }
        output.Position = 0;
        return output;
    }
}
