using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Strategies.Interfaces;
using CloudZCrypt.Infrastructure.Constants;

namespace CloudZCrypt.Infrastructure.Strategies.Compression;

internal class NoCompressionStrategy : ICompressionStrategy
{
    public CompressionMode Id => CompressionMode.None;

    public string DisplayName => "None";

    public string Description =>
        "No compression is applied. Files are encrypted as-is without any size reduction. "
        + "Best when speed is critical or files are already compressed (e.g., ZIP, JPEG, MP4).";

    public string Summary => "No compression (fastest)";

    public Task<Stream> CompressAsync(
        Stream inputStream,
        CancellationToken cancellationToken = default
    )
    {
        MemoryStream output = new();
        return CopyAndRewindAsync(inputStream, output, cancellationToken);
    }

    public Task<Stream> DecompressAsync(
        Stream inputStream,
        CancellationToken cancellationToken = default
    )
    {
        MemoryStream output = new();
        return CopyAndRewindAsync(inputStream, output, cancellationToken);
    }

    private static async Task<Stream> CopyAndRewindAsync(
        Stream input,
        MemoryStream output,
        CancellationToken cancellationToken
    )
    {
        await input.CopyToAsync(output, StreamConstants.CopyBufferSize, cancellationToken);
        output.Position = 0;
        return output;
    }
}
