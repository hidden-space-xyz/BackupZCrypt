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
        MemoryStream inputBuffer = new();
        await inputStream.CopyToAsync(
            inputBuffer,
            StreamConstants.CopyBufferSize,
            cancellationToken
        );
        long uncompressedSize = inputBuffer.Length;
        inputBuffer.Position = 0;

        MemoryStream compressedBuffer = new();
        LzmaEncoderProperties encoderProps = new(false);
        byte[] lzmaProperties;

        using (
            LzmaStream lzma = new(
                encoderProps,
                false,
                new NonClosingStreamWrapper(compressedBuffer)
            )
        )
        {
            await inputBuffer.CopyToAsync(lzma, StreamConstants.CopyBufferSize, cancellationToken);
            lzmaProperties = lzma.Properties;
        }

        long compressedSize = compressedBuffer.Length;

        MemoryStream output = new();
        await output.WriteAsync(lzmaProperties, cancellationToken);
        await output.WriteAsync(BitConverter.GetBytes(uncompressedSize), cancellationToken);
        await output.WriteAsync(BitConverter.GetBytes(compressedSize), cancellationToken);

        compressedBuffer.Position = 0;
        await compressedBuffer.CopyToAsync(
            output,
            StreamConstants.CopyBufferSize,
            cancellationToken
        );

        output.Position = 0;
        return output;
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

        MemoryStream output = new();
        using (LzmaStream lzma = new(properties, inputStream, compressedSize, uncompressedSize))
        {
            await lzma.CopyToAsync(output, StreamConstants.CopyBufferSize, cancellationToken);
        }
        output.Position = 0;
        return output;
    }
}
