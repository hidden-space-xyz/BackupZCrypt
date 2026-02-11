using System.Text;
using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Infrastructure.Strategies.Compression;

namespace CloudZCrypt.Test.Infrastructure.Strategies.Compression;

[TestFixture]
internal sealed class LzmaCompressionStrategyTests
{
    private LzmaCompressionStrategy _strategy = null!;

    [SetUp]
    public void SetUp()
    {
        _strategy = new LzmaCompressionStrategy();
    }

    [Test]
    public void Id_ReturnsLZMA()
    {
        Assert.That(_strategy.Id, Is.EqualTo(CompressionMode.LZMA));
    }

    [Test]
    public void DisplayName_ContainsLZMA()
    {
        Assert.That(_strategy.DisplayName, Does.Contain("LZMA"));
    }

    [Test]
    public void Description_IsNotEmpty()
    {
        Assert.That(_strategy.Description, Is.Not.Empty);
    }

    [Test]
    public void Summary_IsNotEmpty()
    {
        Assert.That(_strategy.Summary, Is.Not.Empty);
    }

    [Test]
    public async Task CompressAndDecompress_RoundTrip_ReturnsOriginalData()
    {
        byte[] original = Encoding.UTF8.GetBytes(
            "This is a test string that should be compressible. " +
            "This is a test string that should be compressible.");
        using MemoryStream input = new(original);

        Stream compressed = await _strategy.CompressAsync(input);
        Stream decompressed = await _strategy.DecompressAsync(compressed);

        byte[] result = new byte[decompressed.Length];
        await decompressed.ReadExactlyAsync(result);

        Assert.That(result, Is.EqualTo(original));
    }

    [Test]
    public async Task CompressAsync_ReducesSize_ForCompressibleData()
    {
        string text = string.Concat(Enumerable.Repeat("ABCDEFGHIJ", 100));
        byte[] data = Encoding.UTF8.GetBytes(text);
        using MemoryStream input = new(data);

        Stream compressed = await _strategy.CompressAsync(input);

        Assert.That(compressed.Length, Is.LessThan(data.Length));
    }

    [Test]
    public async Task CompressAsync_StreamPositionIsZero()
    {
        string text = string.Concat(Enumerable.Repeat("ABCDEFGHIJ", 20));
        using MemoryStream input = new(Encoding.UTF8.GetBytes(text));

        Stream compressed = await _strategy.CompressAsync(input);

        Assert.That(compressed.Position, Is.EqualTo(0));
    }

    [Test]
    public async Task DecompressAsync_StreamPositionIsZero()
    {
        string text = string.Concat(Enumerable.Repeat("ABCDEFGHIJ", 20));
        using MemoryStream input = new(Encoding.UTF8.GetBytes(text));
        Stream compressed = await _strategy.CompressAsync(input);

        Stream decompressed = await _strategy.DecompressAsync(compressed);

        Assert.That(decompressed.Position, Is.EqualTo(0));
    }
}
