namespace BackupZCrypt.Test.Integration;

using BackupZCrypt.Composition;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System.Text;

[TestFixture]
internal sealed class CompressionRoundTripTests
{
    private ServiceProvider provider = null!;

    [SetUp]
    public void SetUp()
    {
        ServiceCollection services = [];
        services.AddDomainServices();
        this.provider = services.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {
        this.provider.Dispose();
    }

    [TestCase(CompressionMode.ZstdFast)]
    [TestCase(CompressionMode.Zstd)]
    [TestCase(CompressionMode.ZstdBest)]
    public async Task AllCompressionStrategies_RoundTrip_PreservesData(CompressionMode mode)
    {
        var strategies = this.provider.GetRequiredService<
            IEnumerable<ICompressionStrategy>
        >();

        var strategy = strategies.First(s => s.Id == mode);

        var original = Encoding.UTF8.GetBytes(
            string.Concat(Enumerable.Repeat("Round trip compression test data! ", 30)));
        await using MemoryStream input = new(original);

        var compressed = await strategy.CompressAsync(input);
        var decompressed = await strategy.DecompressAsync(compressed);

        await using MemoryStream resultStream = new();
        await decompressed.CopyToAsync(resultStream);

        Assert.That(resultStream.ToArray(), Is.EqualTo(original));
    }

    [TestCase(CompressionMode.ZstdFast)]
    [TestCase(CompressionMode.Zstd)]
    [TestCase(CompressionMode.ZstdBest)]
    public async Task CompressionStrategies_ActuallyCompressData(CompressionMode mode)
    {
        var strategies = this.provider.GetRequiredService<
            IEnumerable<ICompressionStrategy>
        >();

        var strategy = strategies.First(s => s.Id == mode);

        var repeatedText = string.Concat(Enumerable.Repeat("AAAAABBBBBCCCCC", 200));
        var original = Encoding.UTF8.GetBytes(repeatedText);
        await using MemoryStream input = new(original);

        var compressed = await strategy.CompressAsync(input);

        Assert.That(
            compressed.Length,
            Is.LessThan(original.Length),
            $"{mode} should compress highly repetitive data");
    }

    [TestCase(CompressionMode.ZstdFast)]
    [TestCase(CompressionMode.Zstd)]
    [TestCase(CompressionMode.ZstdBest)]
    public async Task AllStrategies_EmptyInput_RoundTrip(CompressionMode mode)
    {
        var strategies = this.provider.GetRequiredService<
            IEnumerable<ICompressionStrategy>
        >();

        var strategy = strategies.First(s => s.Id == mode);

        await using MemoryStream input = new([]);

        var compressed = await strategy.CompressAsync(input);
        var decompressed = await strategy.DecompressAsync(compressed);

        await using MemoryStream resultStream = new();
        await decompressed.CopyToAsync(resultStream);

        Assert.That(resultStream.ToArray(), Is.Empty);
    }

    [TestCase(CompressionMode.ZstdFast)]
    [TestCase(CompressionMode.Zstd)]
    [TestCase(CompressionMode.ZstdBest)]
    public void AllStrategies_HaveMetadata(CompressionMode mode)
    {
        var strategies = this.provider.GetRequiredService<
            IEnumerable<ICompressionStrategy>
        >();

        var strategy = strategies.First(s => s.Id == mode);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(strategy.DisplayName, Is.Not.Null.And.Not.Empty);
            Assert.That(strategy.Description, Is.Not.Null.And.Not.Empty);
            Assert.That(strategy.Summary, Is.Not.Null.And.Not.Empty);
        }
    }
}
