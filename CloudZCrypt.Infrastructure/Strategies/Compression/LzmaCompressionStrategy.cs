using CloudZCrypt.Domain.Strategies.Interfaces;
using CloudZCrypt.Infrastructure.Constants;
using CloudZCrypt.Infrastructure.Streams;
using SharpCompress.Compressors.LZMA;

namespace CloudZCrypt.Infrastructure.Strategies.Compression;

internal class LzmaCompressionStrategy : ICompressionStrategy
{
    public Domain.Enums.CompressionMode Id => Domain.Enums.CompressionMode.LZMA;

    public string DisplayName => "LZMA (7-Zip)";

    public string Description =>
        "The Lempel-Ziv-Markov chain Algorithm used by 7-Zip, delivering one of the highest compression ratios "
        + "among general-purpose compressors. Ideal for archiving and scenarios where minimizing file size is "
        + "more important than compression speed.";

    public string Summary => "Highest compression ratio (slowest)";

    public async Task<Stream> CompressAsync(
        Stream inputStream,
        CancellationToken cancellationToken = default
    )
    {
        MemoryStream inputBuffer = new();
        await inputStream.CopyToAsync(inputBuffer, StreamConstants.CopyBufferSize, cancellationToken);
        long uncompressedSize = inputBuffer.Length;
        inputBuffer.Position = 0;

        MemoryStream compressedBuffer = new();
        LzmaEncoderProperties encoderProps = new(false);
        byte[] lzmaProperties;

        using (LzmaStream lzma = new(encoderProps, false, new NonClosingStreamWrapper(compressedBuffer)))
        {
            await inputBuffer.CopyToAsync(lzma, StreamConstants.CopyBufferSize, cancellationToken);
            lzmaProperties = lzma.Properties;
        }

        MemoryStream output = new();
        await output.WriteAsync(lzmaProperties, cancellationToken);
        await output.WriteAsync(BitConverter.GetBytes(uncompressedSize), cancellationToken);

        compressedBuffer.Position = 0;
        await compressedBuffer.CopyToAsync(output, StreamConstants.CopyBufferSize, cancellationToken);

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

        byte[] sizeBytes = new byte[8];
        await inputStream.ReadExactlyAsync(sizeBytes, cancellationToken);
        long uncompressedSize = BitConverter.ToInt64(sizeBytes, 0);

        MemoryStream output = new();
        using (LzmaStream lzma = new(properties, inputStream, uncompressedSize))
        {
            await lzma.CopyToAsync(output, StreamConstants.CopyBufferSize, cancellationToken);
        }
        output.Position = 0;
        return output;
    }
}
