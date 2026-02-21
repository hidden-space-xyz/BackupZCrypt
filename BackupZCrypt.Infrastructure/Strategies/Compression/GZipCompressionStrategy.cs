namespace BackupZCrypt.Infrastructure.Strategies.Compression;

using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Infrastructure.Constants;
using BackupZCrypt.Infrastructure.Resources;
using BackupZCrypt.Infrastructure.Streams;
using SharpCompress.Compressors;
using SharpCompress.Compressors.Deflate;

internal class GZipCompressionStrategy : ICompressionStrategy
{
    public Domain.Enums.CompressionMode Id => Domain.Enums.CompressionMode.GZip;

    public string DisplayName => Messages.GZipDisplayName;

    public string Description => Messages.GZipDescription;

    public string Summary => Messages.GZipSummary;

    public async Task<Stream> CompressAsync(
        Stream inputStream,
        CancellationToken cancellationToken = default)
    {
        FileStream output = CreateTempStream();
        await using (
            GZipStream gzip = new(
                new NonClosingStreamWrapper(output),
                CompressionMode.Compress,
                CompressionLevel.Default))
        {
            await inputStream.CopyToAsync(gzip, StreamConstants.CopyBufferSize, cancellationToken);
        }

        output.Position = 0;
        return output;
    }

    public async Task<Stream> DecompressAsync(
        Stream inputStream,
        CancellationToken cancellationToken = default)
    {
        FileStream output = CreateTempStream();
        await using (GZipStream gzip = new(inputStream, CompressionMode.Decompress))
        {
            await gzip.CopyToAsync(output, StreamConstants.CopyBufferSize, cancellationToken);
        }

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
            });
    }
}
