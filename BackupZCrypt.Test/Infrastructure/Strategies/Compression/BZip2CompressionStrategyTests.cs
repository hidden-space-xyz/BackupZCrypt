namespace BackupZCrypt.Test.Infrastructure.Strategies.Compression;

using System.Text;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Infrastructure.Strategies.Compression;

[TestFixture]
internal sealed class BZip2CompressionStrategyTests
{
    private BZip2CompressionStrategy strategy = null!;

    [SetUp]
    public void SetUp()
    {
        this.strategy = new BZip2CompressionStrategy();
    }

    [Test]
    public void Id_ReturnsBZip2()
    {
        Assert.That(this.strategy.Id, Is.EqualTo(CompressionMode.BZip2));
    }

    [Test]
    public void DisplayName_ReturnsBZip2()
    {
        Assert.That(this.strategy.DisplayName, Is.EqualTo("BZip2"));
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
        await using MemoryStream input = new(Encoding.UTF8.GetBytes("data"));

        Stream compressed = await strategy.CompressAsync(input);

        Assert.That(compressed.Position, Is.EqualTo(0));
    }

    [Test]
    public async Task DecompressAsync_StreamPositionIsZero()
    {
        await using MemoryStream input = new(Encoding.UTF8.GetBytes("data"));
        Stream compressed = await strategy.CompressAsync(input);

        Stream decompressed = await strategy.DecompressAsync(compressed);

        Assert.That(decompressed.Position, Is.EqualTo(0));
    }
}
