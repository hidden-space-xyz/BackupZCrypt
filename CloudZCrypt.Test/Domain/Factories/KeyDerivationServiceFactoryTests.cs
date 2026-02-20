namespace CloudZCrypt.Test.Domain.Factories;

using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Factories;
using CloudZCrypt.Domain.Strategies.Interfaces;
using NSubstitute;

[TestFixture]
internal sealed class KeyDerivationServiceFactoryTests
{
    private KeyDerivationServiceFactory factory = null!;
    private IKeyDerivationAlgorithmStrategy argon2Strategy = null!;

    [SetUp]
    public void SetUp()
    {
        this.argon2Strategy = Substitute.For<IKeyDerivationAlgorithmStrategy>();
        this.argon2Strategy.Id.Returns(KeyDerivationAlgorithm.Argon2id);

        this.factory = new KeyDerivationServiceFactory([this.argon2Strategy]);
    }

    [Test]
    public void Create_RegisteredAlgorithm_ReturnsStrategy()
    {
        IKeyDerivationAlgorithmStrategy result = this.factory.Create(KeyDerivationAlgorithm.Argon2id);

        Assert.That(result, Is.SameAs(this.argon2Strategy));
    }

    [Test]
    public void Create_UnregisteredAlgorithm_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            this.factory.Create(KeyDerivationAlgorithm.PBKDF2));
    }
}
