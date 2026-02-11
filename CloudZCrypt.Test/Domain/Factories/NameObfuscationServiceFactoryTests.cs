using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Factories;
using CloudZCrypt.Domain.Strategies.Interfaces;
using NSubstitute;

namespace CloudZCrypt.Test.Domain.Factories;

[TestFixture]
internal sealed class NameObfuscationServiceFactoryTests
{
    private NameObfuscationServiceFactory _factory = null!;
    private INameObfuscationStrategy _noneStrategy = null!;
    private INameObfuscationStrategy _guidStrategy = null!;

    [SetUp]
    public void SetUp()
    {
        _noneStrategy = Substitute.For<INameObfuscationStrategy>();
        _noneStrategy.Id.Returns(NameObfuscationMode.None);

        _guidStrategy = Substitute.For<INameObfuscationStrategy>();
        _guidStrategy.Id.Returns(NameObfuscationMode.Guid);

        _factory = new NameObfuscationServiceFactory([_noneStrategy, _guidStrategy]);
    }

    [Test]
    public void Create_RegisteredMode_ReturnsStrategy()
    {
        INameObfuscationStrategy result = _factory.Create(NameObfuscationMode.None);

        Assert.That(result, Is.SameAs(_noneStrategy));
    }

    [Test]
    public void Create_GuidMode_ReturnsCorrectStrategy()
    {
        INameObfuscationStrategy result = _factory.Create(NameObfuscationMode.Guid);

        Assert.That(result, Is.SameAs(_guidStrategy));
    }

    [Test]
    public void Create_UnregisteredMode_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _factory.Create(NameObfuscationMode.Sha256));
    }
}
