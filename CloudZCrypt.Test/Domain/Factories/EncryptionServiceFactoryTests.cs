using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Factories;
using CloudZCrypt.Domain.Strategies.Interfaces;
using NSubstitute;

namespace CloudZCrypt.Test.Domain.Factories;

[TestFixture]
internal sealed class EncryptionServiceFactoryTests
{
    private EncryptionServiceFactory _factory = null!;
    private IEncryptionAlgorithmStrategy _aesStrategy = null!;

    [SetUp]
    public void SetUp()
    {
        _aesStrategy = Substitute.For<IEncryptionAlgorithmStrategy>();
        _aesStrategy.Id.Returns(EncryptionAlgorithm.Aes);

        _factory = new EncryptionServiceFactory([_aesStrategy]);
    }

    [Test]
    public void Create_RegisteredAlgorithm_ReturnsStrategy()
    {
        IEncryptionAlgorithmStrategy result = _factory.Create(EncryptionAlgorithm.Aes);

        Assert.That(result, Is.SameAs(_aesStrategy));
    }

    [Test]
    public void Create_UnregisteredAlgorithm_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _factory.Create(EncryptionAlgorithm.Serpent));
    }
}
