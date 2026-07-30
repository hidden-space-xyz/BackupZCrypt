using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Infrastructure.Strategies.Compression;

namespace BackupZCrypt.Test.Unit.Infrastructure;

/// <summary>
/// Unit tests for the Zstandard compression strategies.
/// </summary>
public sealed class CompressionStrategyTests
{
    /// <summary>
    /// Supplies every Zstandard level as a test case so each behaves identically from the caller's view.
    /// </summary>
    /// <returns>One strategy instance per supported compression level.</returns>
    private static IEnumerable<ICompressionStrategy> Levels() =>
        [
            new ZstdFastCompressionStrategy(),
            new ZstdCompressionStrategy(),
            new ZstdBestCompressionStrategy(),
        ];

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

    [TestCaseSource(nameof(Levels))]
    public async Task Roundtrip_RecoversCompressibleData(ICompressionStrategy strategy)
    {
        var original = CompressiblePattern(200 * 1024);

        var compressed = await CompressToBytesAsync(strategy, original);
        var restored = await DecompressToBytesAsync(strategy, compressed);

        Assert.That(restored, Is.EqualTo(original));
    }

    [TestCaseSource(nameof(Levels))]
    public async Task Roundtrip_RecoversRandomData(ICompressionStrategy strategy)
    {
        var original = RandomBytes(64 * 1024, seed: 2024);

        var compressed = await CompressToBytesAsync(strategy, original);
        var restored = await DecompressToBytesAsync(strategy, compressed);

        Assert.That(restored, Is.EqualTo(original));
    }

    [TestCaseSource(nameof(Levels))]
    public async Task Compress_ShrinksCompressibleInput(ICompressionStrategy strategy)
    {
        var original = CompressiblePattern(200 * 1024);

        var compressed = await CompressToBytesAsync(strategy, original);

        Assert.That(
            compressed,
            Has.Length.LessThan(original.Length),
            $"Expected compressed length {compressed.Length} < original {original.Length}."
        );
    }
}
