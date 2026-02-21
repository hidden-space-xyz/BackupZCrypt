namespace BackupZCrypt.Infrastructure.Strategies.Compression;

using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Infrastructure.Constants;
using BackupZCrypt.Infrastructure.Resources;
using BackupZCrypt.Infrastructure.Streams;
using SharpCompress.Compressors;
using SharpCompress.Compressors.BZip2;

internal class BZip2CompressionStrategy : ICompressionStrategy
{
    public Domain.Enums.CompressionMode Id => Domain.Enums.CompressionMode.BZip2;

    public string DisplayName => Messages.BZip2DisplayName;

    public string Description => Messages.BZip2Description;

    public string Summary => Messages.BZip2Summary;

    public async Task<Stream> CompressAsync(
        Stream inputStream,
        CancellationToken cancellationToken = default)
    {
        FileStream output = CreateTempStream();
        await using (
            BZip2Stream bzip2 = await BZip2Stream.CreateAsync(new NonClosingStreamWrapper(output), CompressionMode.Compress, false, false, cancellationToken))
        {
            await inputStream.CopyToAsync(bzip2, StreamConstants.CopyBufferSize, cancellationToken);
        }

        output.Position = 0;
        return output;
    }

    public async Task<Stream> DecompressAsync(
        Stream inputStream,
        CancellationToken cancellationToken = default)
    {
        FileStream output = CreateTempStream();
        await using (BZip2Stream bzip2 = await BZip2Stream.CreateAsync(inputStream, CompressionMode.Decompress, false, false, cancellationToken))
        {
            await bzip2.CopyToAsync(output, StreamConstants.CopyBufferSize, cancellationToken);
        }

        output.Position = 0;
        return output;
    }

    private static FileStream CreateTempStream()
    {
        string tempFilePath = Path.GetRandomFileName();
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
