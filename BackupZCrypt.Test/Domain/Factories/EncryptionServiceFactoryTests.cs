namespace BackupZCrypt.Test.Domain.Factories;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Factories;
using BackupZCrypt.Domain.Strategies.Interfaces;
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
        var result = this.factory.Create(EncryptionAlgorithm.Aes);

        Assert.That(result, Is.SameAs(this.aesStrategy));
    }

    [Test]
    public void Create_UnregisteredAlgorithm_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            this.factory.Create(EncryptionAlgorithm.Serpent));
    }
}
