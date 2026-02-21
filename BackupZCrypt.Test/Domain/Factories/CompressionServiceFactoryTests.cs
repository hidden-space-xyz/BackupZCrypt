namespace BackupZCrypt.Test.Domain.Factories;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Factories;
using BackupZCrypt.Domain.Strategies.Interfaces;
using NSubstitute;

[TestFixture]
internal sealed class CompressionServiceFactoryTests
{
    private CompressionServiceFactory factory = null!;
    private ICompressionStrategy noneStrategy = null!;
    private ICompressionStrategy gzipStrategy = null!;

    [SetUp]
    public void SetUp()
    {
        this.noneStrategy = Substitute.For<ICompressionStrategy>();
        this.noneStrategy.Id.Returns(CompressionMode.None);

        this.gzipStrategy = Substitute.For<ICompressionStrategy>();
        this.gzipStrategy.Id.Returns(CompressionMode.GZip);

        this.factory = new CompressionServiceFactory([this.noneStrategy, this.gzipStrategy]);
    }

    [Test]
    public void Create_RegisteredMode_ReturnsStrategy()
    {
        ICompressionStrategy result = this.factory.Create(CompressionMode.None);

        Assert.That(result, Is.SameAs(this.noneStrategy));
    }

    [Test]
    public void Create_AnotherRegisteredMode_ReturnsCorrectStrategy()
    {
        ICompressionStrategy result = this.factory.Create(CompressionMode.GZip);

        Assert.That(result, Is.SameAs(this.gzipStrategy));
    }

    [Test]
    public void Create_UnregisteredMode_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => this.factory.Create(CompressionMode.LZMA));
    }
}
