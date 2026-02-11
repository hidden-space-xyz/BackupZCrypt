using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Factories;
using CloudZCrypt.Domain.Strategies.Interfaces;
using NSubstitute;

namespace CloudZCrypt.Test.Domain.Factories;

[TestFixture]
internal sealed class CompressionServiceFactoryTests
{
    private CompressionServiceFactory _factory = null!;
    private ICompressionStrategy _noneStrategy = null!;
    private ICompressionStrategy _gzipStrategy = null!;

    [SetUp]
    public void SetUp()
    {
        _noneStrategy = Substitute.For<ICompressionStrategy>();
        _noneStrategy.Id.Returns(CompressionMode.None);

        _gzipStrategy = Substitute.For<ICompressionStrategy>();
        _gzipStrategy.Id.Returns(CompressionMode.GZip);

        _factory = new CompressionServiceFactory([_noneStrategy, _gzipStrategy]);
    }

    [Test]
    public void Create_RegisteredMode_ReturnsStrategy()
    {
        ICompressionStrategy result = _factory.Create(CompressionMode.None);

        Assert.That(result, Is.SameAs(_noneStrategy));
    }

    [Test]
    public void Create_AnotherRegisteredMode_ReturnsCorrectStrategy()
    {
        ICompressionStrategy result = _factory.Create(CompressionMode.GZip);

        Assert.That(result, Is.SameAs(_gzipStrategy));
    }

    [Test]
    public void Create_UnregisteredMode_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _factory.Create(CompressionMode.LZMA));
    }
}
