namespace BackupZCrypt.Test.Infrastructure.Strategies.Compression;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Infrastructure.Strategies.Compression;
using System.Text;

[TestFixture]
internal sealed class ZstdFastCompressionStrategyTests
{
    private ZstdFastCompressionStrategy strategy = null!;

    [SetUp]
    public void SetUp()
    {
        this.strategy = new ZstdFastCompressionStrategy();
    }

    [Test]
    public void Id_ReturnsZstdFast()
    {
        Assert.That(this.strategy.Id, Is.EqualTo(CompressionMode.ZstdFast));
    }

    [Test]
    public void DisplayName_ContainsZstandard()
    {
        Assert.That(this.strategy.DisplayName, Does.Contain("Zstandard"));
    }

    [Test]
    public void Description_IsNotEmpty()
    {
        Assert.That(this.strategy.Description, Is.Not.Empty);
    }

    [Test]
    public void Summary_IsNotEmpty()
    {
        Assert.That(this.strategy.Summary, Is.Not.Empty);
    }

    [Test]
    public async Task CompressAndDecompress_RoundTrip_ReturnsOriginalData()
    {
        byte[] original = Encoding.UTF8.GetBytes(
            "This is a test string that should be compressible. "
                + "This is a test string that should be compressible.");
        await using MemoryStream input = new(original);

        Stream compressed = await strategy.CompressAsync(input);
        Stream decompressed = await strategy.DecompressAsync(compressed);

        var result = new byte[decompressed.Length];
        await decompressed.ReadExactlyAsync(result);

        Assert.That(result, Is.EqualTo(original));
    }

    [Test]
    public async Task CompressAsync_ReducesSize_ForCompressibleData()
    {
        string text = string.Concat(Enumerable.Repeat("ABCDEFGHIJ", 100));
        byte[] data = Encoding.UTF8.GetBytes(text);
        await using MemoryStream input = new(data);

        Stream compressed = await strategy.CompressAsync(input);

        Assert.That(compressed.Length, Is.LessThan(data.Length));
    }

    [Test]
    public async Task CompressAsync_StreamPositionIsZero()
    {
        await using MemoryStream input = new(Encoding.UTF8.GetBytes("data"));

        Stream compressed = await strategy.CompressAsync(input);

        Assert.That(compressed.Position, Is.Zero);
    }

    [Test]
    public async Task DecompressAsync_StreamPositionIsZero()
    {
        await using MemoryStream input = new(Encoding.UTF8.GetBytes("data"));
        Stream compressed = await strategy.CompressAsync(input);

        Stream decompressed = await strategy.DecompressAsync(compressed);

        Assert.That(decompressed.Position, Is.Zero);
    }
}
