using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Factories;
using CloudZCrypt.Domain.Strategies.Interfaces;
using NSubstitute;

namespace CloudZCrypt.Test.Domain.Factories;

[TestFixture]
internal sealed class KeyDerivationServiceFactoryTests
{
    private KeyDerivationServiceFactory _factory = null!;
    private IKeyDerivationAlgorithmStrategy _argon2Strategy = null!;

    [SetUp]
    public void SetUp()
    {
        _argon2Strategy = Substitute.For<IKeyDerivationAlgorithmStrategy>();
        _argon2Strategy.Id.Returns(KeyDerivationAlgorithm.Argon2id);

        _factory = new KeyDerivationServiceFactory([_argon2Strategy]);
    }

    [Test]
    public void Create_RegisteredAlgorithm_ReturnsStrategy()
    {
        IKeyDerivationAlgorithmStrategy result = _factory.Create(KeyDerivationAlgorithm.Argon2id);

        Assert.That(result, Is.SameAs(_argon2Strategy));
    }

    [Test]
    public void Create_UnregisteredAlgorithm_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _factory.Create(KeyDerivationAlgorithm.PBKDF2)
        );
    }
}
