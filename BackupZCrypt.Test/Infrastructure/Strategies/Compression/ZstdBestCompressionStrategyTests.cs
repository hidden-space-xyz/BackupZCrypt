namespace BackupZCrypt.Test.Infrastructure.Strategies.Compression;

using System.Text;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Infrastructure.Strategies.Compression;

[TestFixture]
internal sealed class ZstdBestCompressionStrategyTests
{
    private ZstdBestCompressionStrategy strategy = null!;

    [SetUp]
    public void SetUp()
    {
        this.strategy = new ZstdBestCompressionStrategy();
    }

    [Test]
    public void Id_ReturnsZstdBest()
    {
        Assert.That(this.strategy.Id, Is.EqualTo(CompressionMode.ZstdBest));
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

        byte[] result = new byte[decompressed.Length];
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
        string text = string.Concat(Enumerable.Repeat("ABCDEFGHIJ", 20));
        await using MemoryStream input = new(Encoding.UTF8.GetBytes(text));

        Stream compressed = await strategy.CompressAsync(input);

        Assert.That(compressed.Position, Is.EqualTo(0));
    }

    [Test]
    public async Task DecompressAsync_StreamPositionIsZero()
    {
        string text = string.Concat(Enumerable.Repeat("ABCDEFGHIJ", 20));
        await using MemoryStream input = new(Encoding.UTF8.GetBytes(text));
        Stream compressed = await strategy.CompressAsync(input);

        Stream decompressed = await strategy.DecompressAsync(compressed);

        Assert.That(decompressed.Position, Is.EqualTo(0));
    }
}
