using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Factories;
using BackupZCrypt.Domain.Strategies.Interfaces;
using NSubstitute;

namespace BackupZCrypt.Test.Unit.Domain;

public sealed class CompressionServiceFactoryTests
{
    private static ICompressionStrategy Stub(CompressionMode id)
    {
        var strategy = Substitute.For<ICompressionStrategy>();
        strategy.Id.Returns(id);
        return strategy;
    }

    [Fact]
    public void Create_RegisteredMode_ReturnsMatchingStrategy()
    {
        var none = Stub(CompressionMode.None);
        var zstd = Stub(CompressionMode.Zstd);
        var factory = new CompressionServiceFactory([none, zstd]);

        Assert.Same(none, factory.Create(CompressionMode.None));
        Assert.Same(zstd, factory.Create(CompressionMode.Zstd));
    }

    [Fact]
    public void Create_UnregisteredMode_Throws()
    {
        var factory = new CompressionServiceFactory([Stub(CompressionMode.None)]);

        Assert.Throws<ArgumentOutOfRangeException>(() => factory.Create(CompressionMode.ZstdBest));
    }
}
