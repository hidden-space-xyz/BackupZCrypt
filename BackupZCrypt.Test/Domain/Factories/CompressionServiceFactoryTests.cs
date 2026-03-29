namespace BackupZCrypt.Test.Domain.Factories;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Factories;
using BackupZCrypt.Domain.Strategies.Interfaces;
using NSubstitute;

[TestFixture]
internal sealed class CompressionServiceFactoryTests
{
    private CompressionServiceFactory factory = null!;
    private ICompressionStrategy zstdStrategy = null!;

    [SetUp]
    public void SetUp()
    {
        this.zstdStrategy = Substitute.For<ICompressionStrategy>();
        this.zstdStrategy.Id.Returns(CompressionMode.Zstd);

        this.factory = new CompressionServiceFactory([this.zstdStrategy]);
    }

    [Test]
    public void Create_RegisteredMode_ReturnsStrategy()
    {
        ICompressionStrategy result = this.factory.Create(CompressionMode.Zstd);

        Assert.That(result, Is.SameAs(this.zstdStrategy));
    }

    [Test]
    public void Create_UnregisteredMode_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => this.factory.Create(CompressionMode.ZstdBest));
    }

    [Test]
    public void Create_NoneMode_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => this.factory.Create(CompressionMode.None));
    }
}
