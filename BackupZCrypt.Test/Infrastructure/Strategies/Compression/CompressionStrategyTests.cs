namespace BackupZCrypt.Test.Infrastructure.Strategies.Compression;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Infrastructure.Strategies.Compression;
using System.Text;

[TestFixtureSource(nameof(Strategies))]
internal sealed class CompressionStrategyTests(
    ICompressionStrategy strategy,
    CompressionMode expectedId)
{
    private static IEnumerable<TestFixtureData> Strategies()
    {
        yield return new TestFixtureData(new ZstdFastCompressionStrategy(), CompressionMode.ZstdFast)
            .SetArgDisplayNames("ZstdFast");
        yield return new TestFixtureData(new ZstdCompressionStrategy(), CompressionMode.Zstd)
            .SetArgDisplayNames("Zstd");
        yield return new TestFixtureData(new ZstdBestCompressionStrategy(), CompressionMode.ZstdBest)
            .SetArgDisplayNames("ZstdBest");
    }

    [Test]
    public void Id_ReturnsExpected()
    {
        Assert.That(strategy.Id, Is.EqualTo(expectedId));
    }

    [Test]
    public void DisplayName_ContainsZstandard()
    {
        Assert.That(strategy.DisplayName, Does.Contain("Zstandard"));
    }

    [Test]
    public void Description_IsNotEmpty()
    {
        Assert.That(strategy.Description, Is.Not.Empty);
    }

    [Test]
    public void Summary_IsNotEmpty()
    {
        Assert.That(strategy.Summary, Is.Not.Empty);
    }

    [Test]
    public async Task CompressAndDecompress_RoundTrip_ReturnsOriginalData()
    {
        var original = Encoding.UTF8.GetBytes(
            "This is a test string that should be compressible. "
                + "This is a test string that should be compressible.");
        await using MemoryStream input = new(original);

        var compressed = await strategy.CompressAsync(input);
        var decompressed = await strategy.DecompressAsync(compressed);

        var result = new byte[decompressed.Length];
        await decompressed.ReadExactlyAsync(result);

        Assert.That(result, Is.EqualTo(original));
    }

    [Test]
    public async Task CompressAsync_ReducesSize_ForCompressibleData()
    {
        var text = string.Concat(Enumerable.Repeat("ABCDEFGHIJ", 100));
        var data = Encoding.UTF8.GetBytes(text);
        await using MemoryStream input = new(data);

        var compressed = await strategy.CompressAsync(input);

        Assert.That(compressed.Length, Is.LessThan(data.Length));
    }

    [Test]
    public async Task CompressAsync_StreamPositionIsZero()
    {
        var text = string.Concat(Enumerable.Repeat("ABCDEFGHIJ", 20));
        await using MemoryStream input = new(Encoding.UTF8.GetBytes(text));

        var compressed = await strategy.CompressAsync(input);

        Assert.That(compressed.Position, Is.Zero);
    }

    [Test]
    public async Task DecompressAsync_StreamPositionIsZero()
    {
        var text = string.Concat(Enumerable.Repeat("ABCDEFGHIJ", 20));
        await using MemoryStream input = new(Encoding.UTF8.GetBytes(text));
        var compressed = await strategy.CompressAsync(input);

        var decompressed = await strategy.DecompressAsync(compressed);

        Assert.That(decompressed.Position, Is.Zero);
    }
}
