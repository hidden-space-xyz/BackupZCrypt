namespace CloudZCrypt.Test.Domain.Factories;

using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Factories;
using CloudZCrypt.Domain.Strategies.Interfaces;
using NSubstitute;

[TestFixture]
internal sealed class EncryptionServiceFactoryTests
{
    private EncryptionServiceFactory factory = null!;
    private IEncryptionAlgorithmStrategy aesStrategy = null!;

    [SetUp]
    public void SetUp()
    {
        this.aesStrategy = Substitute.For<IEncryptionAlgorithmStrategy>();
        this.aesStrategy.Id.Returns(EncryptionAlgorithm.Aes);

        this.factory = new EncryptionServiceFactory([this.aesStrategy]);
    }

    [Test]
    public void Create_RegisteredAlgorithm_ReturnsStrategy()
    {
        IEncryptionAlgorithmStrategy result = this.factory.Create(EncryptionAlgorithm.Aes);

        Assert.That(result, Is.SameAs(this.aesStrategy));
    }

    [Test]
    public void Create_UnregisteredAlgorithm_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            this.factory.Create(EncryptionAlgorithm.Serpent));
    }
}
