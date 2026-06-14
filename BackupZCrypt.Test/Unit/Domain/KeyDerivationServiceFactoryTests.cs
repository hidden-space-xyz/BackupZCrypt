using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Factories;
using BackupZCrypt.Domain.Strategies.Interfaces;

using NSubstitute;

namespace BackupZCrypt.Test.Unit.Domain;

public sealed class KeyDerivationServiceFactoryTests
{
    private static IKeyDerivationAlgorithmStrategy Stub(KeyDerivationAlgorithm id)
    {
        var strategy = Substitute.For<IKeyDerivationAlgorithmStrategy>();
        _ = strategy.Id.Returns(id);
        return strategy;
    }

    [Test]
    public void Create_RegisteredAlgorithm_ReturnsMatchingStrategy()
    {
        var argon = Stub(KeyDerivationAlgorithm.Argon2id);
        var pbkdf2 = Stub(KeyDerivationAlgorithm.PBKDF2);
        var factory = new KeyDerivationServiceFactory([argon, pbkdf2]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(factory.Create(KeyDerivationAlgorithm.Argon2id), Is.SameAs(argon));
            Assert.That(factory.Create(KeyDerivationAlgorithm.PBKDF2), Is.SameAs(pbkdf2));
        }
    }

    [Test]
    public void Create_UnregisteredAlgorithm_Throws()
    {
        var factory = new KeyDerivationServiceFactory([Stub(KeyDerivationAlgorithm.Argon2id)]);

        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => factory.Create(KeyDerivationAlgorithm.Scrypt)
        );
    }
}
