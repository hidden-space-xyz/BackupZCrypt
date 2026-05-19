namespace BackupZCrypt.Infrastructure.Strategies.Chunking;

using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Strategies.Interfaces;
using System.Buffers;
using System.Runtime.CompilerServices;

internal sealed class FastCdcChunker : IContentChunker
{
    private const int GearTableSize = 256;
    private const int MaskBits = 20;
    private const ulong MaskLarge = (1UL << (MaskBits + 1)) - 1;
    private const ulong MaskSmall = (1UL << (MaskBits - 1)) - 1;

    private static readonly ulong[] GearTable = GenerateGearTable();

    public async IAsyncEnumerable<ReadOnlyMemory<byte>> ChunkAsync(
        Stream source,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var minSize = BackupConstants.ChunkMinSize;
        var maxSize = BackupConstants.ChunkMaxSize;
        var targetSize = BackupConstants.ChunkTargetSize;

        var readBuffer = ArrayPool<byte>.Shared.Rent(maxSize * 2);

        try
        {
            var bufferedLength = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var bytesRead = await source.ReadAsync(
                    readBuffer.AsMemory(bufferedLength, readBuffer.Length - bufferedLength),
                    cancellationToken);

                bufferedLength += bytesRead;

                if (bufferedLength == 0)
                {
                    yield break;
                }

                var isEof = bytesRead == 0;

                while (bufferedLength >= minSize || (isEof && bufferedLength > 0))
                {
                    var chunkSize = FindChunkBoundary(
                        readBuffer.AsSpan(0, bufferedLength),
                        minSize,
                        maxSize,
                        targetSize);

                    if (chunkSize == 0 || (!isEof && chunkSize == bufferedLength && bufferedLength < maxSize))
                    {
                        break;
                    }

                    var chunk = new byte[chunkSize];
                    readBuffer.AsSpan(0, chunkSize).CopyTo(chunk);
                    yield return chunk;

                    var remaining = bufferedLength - chunkSize;
                    if (remaining > 0)
                    {
                        Buffer.BlockCopy(readBuffer, chunkSize, readBuffer, 0, remaining);
                    }

                    bufferedLength = remaining;
                }

                if (isEof)
                {
                    if (bufferedLength > 0)
                    {
                        var finalChunk = new byte[bufferedLength];
                        readBuffer.AsSpan(0, bufferedLength).CopyTo(finalChunk);
                        yield return finalChunk;
                    }

                    yield break;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(readBuffer, clearArray: true);
        }
    }

    private static int FindChunkBoundary(
        ReadOnlySpan<byte> data,
        int minSize,
        int maxSize,
        int targetSize)
    {
        var length = Math.Min(data.Length, maxSize);

        if (length <= minSize)
        {
            return length;
        }

        ulong hash = 0;

        // Skip to minimum size (no boundary before min)
        var start = minSize;
        var midpoint = Math.Min(targetSize, length);

        // First pass: from minSize to targetSize, use the smaller mask (harder to match = larger chunks)
        for (var i = start; i < midpoint; i++)
        {
            hash = (hash << 1) + GearTable[data[i]];
            if ((hash & MaskSmall) == 0)
            {
                return i + 1;
            }
        }

        // Second pass: from targetSize to maxSize, use the larger mask (easier to match = cut sooner)
        for (var i = midpoint; i < length; i++)
        {
            hash = (hash << 1) + GearTable[data[i]];
            if ((hash & MaskLarge) == 0)
            {
                return i + 1;
            }
        }

        return length;
    }

    private static ulong[] GenerateGearTable()
    {
        var table = new ulong[GearTableSize];

        // Deterministic pseudo-random table using a simple LCG seeded with a fixed value
        var state = 0x123456789ABCDEF0UL;
        for (var i = 0; i < GearTableSize; i++)
        {
            state ^= state << 13;
            state ^= state >> 7;
            state ^= state << 17;
            table[i] = state;
        }

        return table;
    }
}
