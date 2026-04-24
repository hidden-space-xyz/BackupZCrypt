namespace BackupZCrypt.Infrastructure.Strategies.Compression;

using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Infrastructure.Constants;
using BackupZCrypt.Infrastructure.Streams;
using ZstdSharp;

internal abstract class ZstdCompressionStrategyBase : ICompressionStrategy
{
    public abstract Domain.Enums.CompressionMode Id { get; }

    public abstract string DisplayName { get; }

    public abstract string Description { get; }

    public abstract string Summary { get; }

    protected abstract int CompressionLevel { get; }

    public async Task<Stream> CompressAsync(
        Stream inputStream,
        CancellationToken cancellationToken = default)
    {
        var output = CreateTempStream();
        await using (
            CompressionStream zstd = new(
                new NonClosingStreamWrapper(output),
                CompressionLevel))
        {
            await inputStream.CopyToAsync(zstd, StreamConstants.CopyBufferSize, cancellationToken);
        }

        output.Position = 0;
        return output;
    }

    public async Task<Stream> DecompressAsync(
        Stream inputStream,
        CancellationToken cancellationToken = default)
    {
        var output = CreateTempStream();
        await using (DecompressionStream zstd = new(inputStream))
        {
            await zstd.CopyToAsync(output, StreamConstants.CopyBufferSize, cancellationToken);
        }

        output.Position = 0;
        return output;
    }

    private static FileStream CreateTempStream()
    {
        var tempFilePath = Path.GetTempFileName();
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
            });
    }
}
