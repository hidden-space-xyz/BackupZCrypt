namespace CloudZCrypt.Test.Infrastructure.Strategies.Compression;

using System.Text;
using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Infrastructure.Strategies.Compression;

[TestFixture]
internal sealed class NoCompressionStrategyTests
{
    private NoCompressionStrategy strategy = null!;

    [SetUp]
    public void SetUp()
    {
        this.strategy = new NoCompressionStrategy();
    }

    [Test]
    public void Id_ReturnsNone()
    {
        Assert.That(this.strategy.Id, Is.EqualTo(CompressionMode.None));
    }

    [Test]
    public void DisplayName_ReturnsNone()
    {
        Assert.That(this.strategy.DisplayName, Is.EqualTo("None"));
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
    public async Task CompressAsync_ReturnsIdenticalData()
    {
        byte[] data = Encoding.UTF8.GetBytes("Hello, world!");
        using MemoryStream input = new(data);

        Stream result = await this.strategy.CompressAsync(input);

        byte[] output = new byte[result.Length];
        await result.ReadExactlyAsync(output);

        Assert.That(output, Is.EqualTo(data));
    }

    [Test]
    public async Task DecompressAsync_ReturnsIdenticalData()
    {
        byte[] data = Encoding.UTF8.GetBytes("Hello, world!");
        using MemoryStream input = new(data);

        Stream result = await this.strategy.DecompressAsync(input);

        byte[] output = new byte[result.Length];
        await result.ReadExactlyAsync(output);

        Assert.That(output, Is.EqualTo(data));
    }

    [Test]
    public async Task CompressAsync_StreamPositionIsZero()
    {
        using MemoryStream input = new([1, 2, 3]);

        Stream result = await this.strategy.CompressAsync(input);

        Assert.That(result.Position, Is.EqualTo(0));
    }

    [Test]
    public async Task CompressAsync_EmptyStream_ReturnsEmptyStream()
    {
        using MemoryStream input = new();

        Stream result = await this.strategy.CompressAsync(input);

        Assert.That(result.Length, Is.EqualTo(0));
    }
}
