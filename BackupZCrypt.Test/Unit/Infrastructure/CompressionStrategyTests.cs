using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Infrastructure.Strategies.Compression;

namespace BackupZCrypt.Test.Unit.Infrastructure;

/// <summary>
/// Unit tests for the Zstandard compression strategies.
/// </summary>
/// <remarks>
/// <para>
/// The three modes share a base class and differ only in the level they hand to zstd, so nothing but
/// an output-size comparison would notice a level swapped between the fastest and the best strategy.
/// That comparison is only ever made between the two extremes: zstd guarantees no monotonic
/// relationship between the output sizes of adjacent levels, and measurement confirms it. On a
/// 256 KiB sample <see cref="ZstdFastCompressionStrategy"/> and
/// <see cref="ZstdCompressionStrategy"/> tie exactly on uniform data (48 bytes against 48), and Zstd
/// comes out 19 bytes larger than ZstdFast on semi-compressible data (6645 against 6626). An
/// assertion that one level beats its neighbour would therefore fail on a valid library upgrade,
/// whereas the extremes differ by roughly 11%, which is a real and stable signal.
/// </para>
/// <para>
/// Decompression runs on restore, so a damaged or truncated frame has to fail loudly rather than hand
/// back a short buffer that would be written to disk as a silently truncated file. The concrete
/// exception type belongs to the zstd binding, so only the failure itself is asserted.
/// </para>
/// </remarks>
public sealed class CompressionStrategyTests
{
    /// <summary>
    /// Supplies every Zstandard level as a theory case so each behaves identically from the caller's
    /// view. Public because <c>[MemberData]</c> sources have to be publicly reachable.
    /// </summary>
    /// <returns>One theory case per supported compression level.</returns>
    public static TheoryData<ICompressionStrategy> Levels()
    {
        return new(CreateLevels());
    }

    /// <summary>
    /// Builds one strategy instance per supported Zstandard level, for the tests that need the
    /// strategies themselves rather than one theory case each.
    /// </summary>
    /// <returns>One strategy instance per supported compression level.</returns>
    private static ICompressionStrategy[] CreateLevels()
    {
        return
        [
            new ZstdFastCompressionStrategy(),
            new ZstdCompressionStrategy(),
            new ZstdBestCompressionStrategy(),
        ];
    }

    /// <summary>
    /// Produces incompressible-looking bytes from a fixed seed so failures reproduce exactly.
    /// </summary>
    /// <param name="length">The number of bytes to produce.</param>
    /// <param name="seed">The seed that makes the sequence deterministic.</param>
    /// <returns>A buffer of pseudo-random bytes.</returns>
    private static byte[] RandomBytes(int length, int seed)
    {
        var data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }

    /// <summary>
    /// Produces a repeating 16-byte ramp, a highly redundant pattern any level should shrink.
    /// </summary>
    /// <param name="length">The number of bytes to produce.</param>
    /// <returns>A buffer whose bytes cycle from 0 through 15.</returns>
    private static byte[] CompressiblePattern(int length)
    {
        var data = new byte[length];
        for (var i = 0; i < length; i++)
        {
            data[i] = (byte)(i % 16);
        }

        return data;
    }

    /// <summary>
    /// Produces partly redundant data: a random ordering of blocks drawn from a small dictionary,
    /// so the matches a higher level finds are measurably longer than the ones the fastest level
    /// settles for. A uniform pattern would compress to the same size at every level and could not
    /// tell the levels apart.
    /// </summary>
    /// <param name="length">The number of bytes to produce.</param>
    /// <param name="seed">The seed that makes the block ordering deterministic.</param>
    /// <returns>A buffer built by repeating sixteen distinct random blocks in a random order.</returns>
    private static byte[] SemiCompressiblePattern(int length, int seed)
    {
        var blocks = new byte[16][];
        for (var i = 0; i < blocks.Length; i++)
        {
            blocks[i] = RandomBytes(256, seed + i + 1);
        }

        var picker = new Random(seed);
        var data = new byte[length];
        var offset = 0;

        while (offset < length)
        {
            var block = blocks[picker.Next(blocks.Length)];
            var count = Math.Min(block.Length, length - offset);
            block.AsSpan(0, count).CopyTo(data.AsSpan(offset));
            offset += count;
        }

        return data;
    }

    /// <summary>
    /// Compresses a buffer and drains the resulting stream so tests can assert on the raw bytes.
    /// </summary>
    /// <param name="strategy">The compression strategy to exercise.</param>
    /// <param name="input">The bytes to compress.</param>
    /// <returns>The compressed representation of <paramref name="input"/>.</returns>
    private static async Task<byte[]> CompressToBytesAsync(
        ICompressionStrategy strategy,
        byte[] input
    )
    {
        await using var compressed = await strategy.CompressAsync(new MemoryStream(input));
        await using MemoryStream collected = new();
        await compressed.CopyToAsync(collected);
        return collected.ToArray();
    }

    /// <summary>
    /// Decompresses a buffer and drains the resulting stream so tests can compare it to the original.
    /// </summary>
    /// <param name="strategy">The compression strategy to exercise.</param>
    /// <param name="compressed">The bytes previously produced by <see cref="CompressToBytesAsync"/>.</param>
    /// <returns>The recovered plaintext bytes.</returns>
    private static async Task<byte[]> DecompressToBytesAsync(
        ICompressionStrategy strategy,
        byte[] compressed
    )
    {
        await using var decompressed = await strategy.DecompressAsync(new MemoryStream(compressed));
        await using MemoryStream collected = new();
        await decompressed.CopyToAsync(collected);
        return collected.ToArray();
    }

    [Theory]
    [MemberData(nameof(Levels))]
    internal async Task Roundtrip_RecoversCompressibleData(ICompressionStrategy strategy)
    {
        var original = CompressiblePattern(200 * 1024);

        var compressed = await CompressToBytesAsync(strategy, original);
        var restored = await DecompressToBytesAsync(strategy, compressed);

        Assert.Equal(original, restored);
    }

    [Theory]
    [MemberData(nameof(Levels))]
    internal async Task Roundtrip_IncompressibleData_RecoversItWithBoundedExpansion(
        ICompressionStrategy strategy
    )
    {
        var original = RandomBytes(64 * 1024, seed: 2024);

        var compressed = await CompressToBytesAsync(strategy, original);
        var restored = await DecompressToBytesAsync(strategy, compressed);

        Assert.Multiple(
            () => Assert.Equal(original, restored),
            () =>
                Assert.True(
                    compressed.Length <= original.Length + 1024,
                    $"Incompressible input grew from {original.Length} to {compressed.Length} bytes. "
                        + "Already-compressed chunks (media, archives) expand rather than shrink and the "
                        + "backup stores the expanded result, so the overhead has to stay a small "
                        + "constant."
                )
        );
    }

    [Theory]
    [MemberData(nameof(Levels))]
    internal async Task Compress_ShrinksCompressibleInput(ICompressionStrategy strategy)
    {
        var original = CompressiblePattern(200 * 1024);

        var compressed = await CompressToBytesAsync(strategy, original);

        Assert.True(
            compressed.Length < original.Length,
            $"Expected compressed length {compressed.Length} < original {original.Length}."
        );
    }

    [Fact]
    internal async Task Compress_BestLevel_ProducesSmallerOutputThanFastLevel()
    {
        var original = SemiCompressiblePattern(256 * 1024, seed: 5150);

        var fast = await CompressToBytesAsync(new ZstdFastCompressionStrategy(), original);
        var best = await CompressToBytesAsync(new ZstdBestCompressionStrategy(), original);

        Assert.True(
            best.Length < fast.Length,
            $"ZstdBest ({best.Length} bytes) did not beat ZstdFast ({fast.Length} bytes), "
                + "so the two levels are probably swapped or identical, which no round-trip test "
                + "would catch."
        );
    }

    [Fact]
    internal async Task Decompress_FrameWrittenByAnotherLevel_RecoversOriginal()
    {
        var original = CompressiblePattern(32 * 1024);
        var levels = CreateLevels();

        foreach (var writer in levels)
        {
            var compressed = await CompressToBytesAsync(writer, original);

            foreach (var reader in levels)
            {
                var restored = await DecompressToBytesAsync(reader, compressed);

                Assert.Equal(original, restored);
            }
        }
    }

    [Fact]
    internal async Task Decompress_CorruptOrTruncatedFrame_FailsInsteadOfReturningPartialData()
    {
        var strategy = new ZstdCompressionStrategy();
        var compressed = await CompressToBytesAsync(strategy, CompressiblePattern(32 * 1024));
        var truncated = compressed[..(compressed.Length / 2)];
        var garbage = RandomBytes(1024, seed: 777);

        _ = await Assert.ThrowsAnyAsync<Exception>(
            async () => await DecompressToBytesAsync(strategy, truncated)
        );
        _ = await Assert.ThrowsAnyAsync<Exception>(
            async () => await DecompressToBytesAsync(strategy, garbage)
        );
    }
}
