using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Strategies.Interfaces;
using CloudZCrypt.Infrastructure.Constants;
using CloudZCrypt.Infrastructure.Resources;

namespace CloudZCrypt.Infrastructure.Strategies.Compression;

internal class NoCompressionStrategy : ICompressionStrategy
{
    public CompressionMode Id => CompressionMode.None;

    public string DisplayName => Messages.NoCompressionDisplayName;

    public string Description => Messages.NoCompressionDescription;

    public string Summary => Messages.NoCompressionSummary;

    public Task<Stream> CompressAsync(
        Stream inputStream,
        CancellationToken cancellationToken = default
    )
    {
        FileStream output = CreateTempStream();
        return CopyAndRewindAsync(inputStream, output, cancellationToken);
    }

    public Task<Stream> DecompressAsync(
        Stream inputStream,
        CancellationToken cancellationToken = default
    )
    {
        FileStream output = CreateTempStream();
        return CopyAndRewindAsync(inputStream, output, cancellationToken);
    }

    private static async Task<Stream> CopyAndRewindAsync(
        Stream input,
        FileStream output,
        CancellationToken cancellationToken
    )
    {
        await input.CopyToAsync(output, StreamConstants.CopyBufferSize, cancellationToken);
        output.Position = 0;
        return output;
    }

    private static FileStream CreateTempStream()
    {
        string tempFilePath = Path.GetTempFileName();
        return new FileStream(
            tempFilePath,
            new FileStreamOptions
            {
                Access = FileAccess.ReadWrite,
                Mode = FileMode.Create,
                Options =
                    FileOptions.Asynchronous
                    | FileOptions.SequentialScan
                    | FileOptions.DeleteOnClose,
                BufferSize = StreamConstants.CopyBufferSize,
            }
        );
    }
}
