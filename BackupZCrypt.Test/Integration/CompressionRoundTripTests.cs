namespace BackupZCrypt.Test.Integration;

using System.Text;
using BackupZCrypt.Composition;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;
using Microsoft.Extensions.DependencyInjection;

[TestFixture]
internal sealed class CompressionRoundTripTests
{
    private ServiceProvider provider = null!;

    [SetUp]
    public void SetUp()
    {
        ServiceCollection services = new();
        services.AddDomainServices();
        this.provider = services.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {
        this.provider.Dispose();
    }

    [TestCase(CompressionMode.None)]
    [TestCase(CompressionMode.GZip)]
    [TestCase(CompressionMode.BZip2)]
    public async Task AllCompressionStrategies_RoundTrip_PreservesData(CompressionMode mode)
    {
        IEnumerable<ICompressionStrategy> strategies = this.provider.GetRequiredService<
            IEnumerable<ICompressionStrategy>
        >();

        ICompressionStrategy strategy = strategies.First(s => s.Id == mode);

        byte[] original = Encoding.UTF8.GetBytes(
            string.Concat(Enumerable.Repeat("Round trip compression test data! ", 30)));
        await using MemoryStream input = new(original);

        Stream compressed = await strategy.CompressAsync(input);
        Stream decompressed = await strategy.DecompressAsync(compressed);

        await using MemoryStream resultStream = new();
        await decompressed.CopyToAsync(resultStream);

        Assert.That(resultStream.ToArray(), Is.EqualTo(original));
    }

    [TestCase(CompressionMode.GZip)]
    [TestCase(CompressionMode.BZip2)]
    [TestCase(CompressionMode.LZMA)]
    public async Task CompressionStrategies_ActuallyCompressData(CompressionMode mode)
    {
        IEnumerable<ICompressionStrategy> strategies = this.provider.GetRequiredService<
            IEnumerable<ICompressionStrategy>
        >();

        ICompressionStrategy strategy = strategies.First(s => s.Id == mode);

        string repeatedText = string.Concat(Enumerable.Repeat("AAAAABBBBBCCCCC", 200));
        byte[] original = Encoding.UTF8.GetBytes(repeatedText);
        await using MemoryStream input = new(original);

        Stream compressed = await strategy.CompressAsync(input);

        Assert.That(
            compressed.Length,
            Is.LessThan(original.Length),
            $"{mode} should compress highly repetitive data");
    }

    [TestCase(CompressionMode.None)]
    [TestCase(CompressionMode.GZip)]
    [TestCase(CompressionMode.BZip2)]
    public async Task AllStrategies_EmptyInput_RoundTrip(CompressionMode mode)
    {
        IEnumerable<ICompressionStrategy> strategies = this.provider.GetRequiredService<
            IEnumerable<ICompressionStrategy>
        >();

        ICompressionStrategy strategy = strategies.First(s => s.Id == mode);

        await using MemoryStream input = new([]);

        Stream compressed = await strategy.CompressAsync(input);
        Stream decompressed = await strategy.DecompressAsync(compressed);

        await using MemoryStream resultStream = new();
        await decompressed.CopyToAsync(resultStream);

        Assert.That(resultStream.ToArray(), Is.Empty);
    }

    [TestCase(CompressionMode.None)]
    [TestCase(CompressionMode.GZip)]
    [TestCase(CompressionMode.BZip2)]
    [TestCase(CompressionMode.LZMA)]
    public void AllStrategies_HaveMetadata(CompressionMode mode)
    {
        IEnumerable<ICompressionStrategy> strategies = this.provider.GetRequiredService<
            IEnumerable<ICompressionStrategy>
        >();

        ICompressionStrategy strategy = strategies.First(s => s.Id == mode);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(strategy.DisplayName, Is.Not.Null.And.Not.Empty);
            Assert.That(strategy.Description, Is.Not.Null.And.Not.Empty);
            Assert.That(strategy.Summary, Is.Not.Null.And.Not.Empty);
        }
    }
}
