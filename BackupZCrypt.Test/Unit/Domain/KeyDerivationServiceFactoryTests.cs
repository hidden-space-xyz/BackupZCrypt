using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Factories;
using BackupZCrypt.Domain.Strategies.Interfaces;

using NSubstitute;

namespace BackupZCrypt.Test.Unit.Domain;

/// <summary>
/// Unit tests for the key-derivation service factory's strategy resolution. The successful lookups
/// are covered end to end against the real container by
/// <see cref="BackupZCrypt.Test.Unit.Composition.StrategyRegistrationTests"/>; what only a
/// substituted strategy set can show is what the factory does when the requested algorithm has no
/// strategy at all.
/// </summary>
/// <remarks>
/// Deriving the master key with a different function than the manifest records makes the archive
/// permanently undecryptable, so an unknown algorithm has to fail instead.
/// </remarks>
public sealed class KeyDerivationServiceFactoryTests
{
    /// <summary>
    /// Creates a substitute strategy that advertises the given algorithm, which is all the factory
    /// indexes on.
    /// </summary>
    /// <param name="id">The key-derivation algorithm the stub reports as its identifier.</param>
    /// <returns>A substitute strategy whose <see cref="IKeyDerivationAlgorithmStrategy.Id"/> reports <paramref name="id"/>.</returns>
    private static IKeyDerivationAlgorithmStrategy Stub(KeyDerivationAlgorithm id)
    {
        var strategy = Substitute.For<IKeyDerivationAlgorithmStrategy>();
        _ = strategy.Id.Returns(id);
        return strategy;
    }

    [Fact]
    internal void Create_UnregisteredAlgorithm_Throws()
    {
        var factory = new KeyDerivationServiceFactory([Stub(KeyDerivationAlgorithm.Argon2id)]);

        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => factory.Create(KeyDerivationAlgorithm.Scrypt)
        );
    }
}
