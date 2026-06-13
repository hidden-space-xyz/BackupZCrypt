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
        strategy.Id.Returns(id);
        return strategy;
    }

    [Fact]
    public void Create_RegisteredAlgorithm_ReturnsMatchingStrategy()
    {
        var argon = Stub(KeyDerivationAlgorithm.Argon2id);
        var pbkdf2 = Stub(KeyDerivationAlgorithm.PBKDF2);
        var factory = new KeyDerivationServiceFactory([argon, pbkdf2]);

        Assert.Same(argon, factory.Create(KeyDerivationAlgorithm.Argon2id));
        Assert.Same(pbkdf2, factory.Create(KeyDerivationAlgorithm.PBKDF2));
    }

    [Fact]
    public void Create_UnregisteredAlgorithm_Throws()
    {
        var factory = new KeyDerivationServiceFactory([Stub(KeyDerivationAlgorithm.Argon2id)]);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => factory.Create(KeyDerivationAlgorithm.Scrypt)
        );
    }
}
