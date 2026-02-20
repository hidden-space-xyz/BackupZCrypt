using CloudZCrypt.Domain.Strategies.Interfaces;
using CloudZCrypt.Infrastructure.Constants;
using CloudZCrypt.Infrastructure.Resources;
using CloudZCrypt.Infrastructure.Streams;
using SharpCompress.Compressors.LZMA;

namespace CloudZCrypt.Infrastructure.Strategies.Compression;

internal class LzmaCompressionStrategy : ICompressionStrategy
{
    public Domain.Enums.CompressionMode Id => Domain.Enums.CompressionMode.LZMA;

    public string DisplayName => Messages.LzmaDisplayName;

    public string Description => Messages.LzmaDescription;

    public string Summary => Messages.LzmaSummary;

    public async Task<Stream> CompressAsync(
        Stream inputStream,
        CancellationToken cancellationToken = default
    )
    {
        const int headerSize = 5 + sizeof(long) + sizeof(long);
        Stream? tempInput = null;
        Stream effectiveInput = inputStream;

        try
        {
            long uncompressedSize;
            if (inputStream.CanSeek)
            {
                uncompressedSize = inputStream.Length - inputStream.Position;
            }
            else
            {
                tempInput = CreateTempStream();
                await inputStream.CopyToAsync(
                    tempInput,
                    StreamConstants.CopyBufferSize,
                    cancellationToken
                );
                tempInput.Position = 0;
                effectiveInput = tempInput;
                uncompressedSize = tempInput.Length;
            }

            FileStream output = CreateTempStream();
            output.Position = headerSize;

            LzmaEncoderProperties encoderProps = new(false);
            byte[] lzmaProperties;

            using (LzmaStream lzma = new(encoderProps, false, new NonClosingStreamWrapper(output)))
            {
                await effectiveInput.CopyToAsync(
                    lzma,
                    StreamConstants.CopyBufferSize,
                    cancellationToken
                );
                lzmaProperties = lzma.Properties;
            }

            long compressedSize = output.Length - headerSize;

            output.Position = 0;
            await output.WriteAsync(lzmaProperties, cancellationToken);
            await output.WriteAsync(BitConverter.GetBytes(uncompressedSize), cancellationToken);
            await output.WriteAsync(BitConverter.GetBytes(compressedSize), cancellationToken);
            output.Position = 0;
            return output;
        }
        finally
        {
            tempInput?.Dispose();
        }
    }

    public async Task<Stream> DecompressAsync(
        Stream inputStream,
        CancellationToken cancellationToken = default
    )
    {
        byte[] properties = new byte[5];
        await inputStream.ReadExactlyAsync(properties, cancellationToken);

        byte[] uncompressedSizeBytes = new byte[8];
        await inputStream.ReadExactlyAsync(uncompressedSizeBytes, cancellationToken);
        long uncompressedSize = BitConverter.ToInt64(uncompressedSizeBytes, 0);

        byte[] compressedSizeBytes = new byte[8];
        await inputStream.ReadExactlyAsync(compressedSizeBytes, cancellationToken);
        long compressedSize = BitConverter.ToInt64(compressedSizeBytes, 0);

        FileStream output = CreateTempStream();
        using (LzmaStream lzma = new(properties, inputStream, compressedSize, uncompressedSize))
        {
            await lzma.CopyToAsync(output, StreamConstants.CopyBufferSize, cancellationToken);
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
