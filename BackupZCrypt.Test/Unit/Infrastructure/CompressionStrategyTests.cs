using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Infrastructure.Strategies.Compression;

namespace BackupZCrypt.Test.Unit.Infrastructure;

public sealed class CompressionStrategyTests
{
    private static IEnumerable<ICompressionStrategy> Levels() =>
        [
            new ZstdFastCompressionStrategy(),
            new ZstdCompressionStrategy(),
            new ZstdBestCompressionStrategy(),
        ];

    private static byte[] RandomBytes(int length, int seed)
    {
        var data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }

    private static byte[] CompressiblePattern(int length)
    {
        var data = new byte[length];
        for (var i = 0; i < length; i++)
        {
            data[i] = (byte)(i % 16);
        }

        return data;
    }

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
