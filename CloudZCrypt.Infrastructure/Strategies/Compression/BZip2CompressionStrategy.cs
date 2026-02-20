using CloudZCrypt.Domain.Strategies.Interfaces;
using CloudZCrypt.Infrastructure.Constants;
using CloudZCrypt.Infrastructure.Resources;
using CloudZCrypt.Infrastructure.Streams;
using SharpCompress.Compressors;
using SharpCompress.Compressors.BZip2;

namespace CloudZCrypt.Infrastructure.Strategies.Compression;

internal class BZip2CompressionStrategy : ICompressionStrategy
{
    public Domain.Enums.CompressionMode Id => Domain.Enums.CompressionMode.BZip2;

    public string DisplayName => Messages.BZip2DisplayName;

    public string Description => Messages.BZip2Description;

    public string Summary => Messages.BZip2Summary;

    public async Task<Stream> CompressAsync(
        Stream inputStream,
        CancellationToken cancellationToken = default
    )
    {
        FileStream output = CreateTempStream();
        using (
            BZip2Stream bzip2 = new(
                new NonClosingStreamWrapper(output),
                CompressionMode.Compress,
                false
            )
        )
        {
            await inputStream.CopyToAsync(bzip2, StreamConstants.CopyBufferSize, cancellationToken);
        }
        output.Position = 0;
        return output;
    }

    public async Task<Stream> DecompressAsync(
        Stream inputStream,
        CancellationToken cancellationToken = default
    )
    {
        FileStream output = CreateTempStream();
        using (BZip2Stream bzip2 = new(inputStream, CompressionMode.Decompress, false))
        {
            await bzip2.CopyToAsync(output, StreamConstants.CopyBufferSize, cancellationToken);
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
            }
        );
    }
}
