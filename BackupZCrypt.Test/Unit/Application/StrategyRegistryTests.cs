using BackupZCrypt.Application.Algorithms;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;

using NSubstitute;

namespace BackupZCrypt.Test.Unit.Application;

/// <summary>
/// Unit tests for the strategy registry's lookup and the identifiers it lists. The successful lookups of every registered
/// algorithm are covered end to end against the real container by
/// <see cref="BackupZCrypt.Test.Unit.Composition.StrategyRegistrationTests"/>; what only a
/// substituted strategy set can show is what the registry does when the requested identifier has no
/// strategy, when two strategies claim the same one, or when the family is empty.
/// </summary>
public sealed class StrategyRegistryTests
{
    [Fact]
    internal void Resolve_RegisteredIdentifier_ReturnsThatInstance()
    {
        var zstd = Stub<CompressionMode, ICompressionStrategy>(CompressionMode.Zstd);
        var registry = new StrategyRegistry<CompressionMode, ICompressionStrategy>(
            [Stub<CompressionMode, ICompressionStrategy>(CompressionMode.ZstdFast), zstd]
        );

        Assert.Same(zstd, registry.Resolve(CompressionMode.Zstd));
    }

    /// <summary>
    /// Encrypting with an algorithm the manifest does not name would make the archive
    /// undecryptable, so an unregistered identifier has to fail instead of falling back to whichever
    /// strategy happens to be registered.
    /// </summary>
    [Fact]
    internal void Resolve_UnregisteredIdentifier_ThrowsInsteadOfSubstitutingAnotherStrategy()
    {
        var registry = new StrategyRegistry<EncryptionAlgorithm, IEncryptionAlgorithmStrategy>(
            [Stub<EncryptionAlgorithm, IEncryptionAlgorithmStrategy>(EncryptionAlgorithm.Aes)]
        );

        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => registry.Resolve(EncryptionAlgorithm.Serpent)
        );

        Assert.Equal(EncryptionAlgorithm.Serpent, exception.ActualValue);
    }

    [Fact]
    internal void Ids_ListTheRegisteredIdentifiersInIdentifierOrder()
    {
        var registry = new StrategyRegistry<CompressionMode, ICompressionStrategy>(
            [
                Stub<CompressionMode, ICompressionStrategy>(CompressionMode.ZstdBest),
                Stub<CompressionMode, ICompressionStrategy>(CompressionMode.ZstdFast),
            ]
        );

        Assert.Equal([CompressionMode.ZstdFast, CompressionMode.ZstdBest], registry.Ids);
    }

    [Fact]
    internal void Constructor_TwoStrategiesWithTheSameIdentifier_Throws()
    {
        _ = Assert.Throws<ArgumentException>(
            () => new StrategyRegistry<EncryptionAlgorithm, IEncryptionAlgorithmStrategy>(
                [
                    Stub<EncryptionAlgorithm, IEncryptionAlgorithmStrategy>(EncryptionAlgorithm.Aes),
                    Stub<EncryptionAlgorithm, IEncryptionAlgorithmStrategy>(EncryptionAlgorithm.Aes),
                ]
            )
        );
    }

    [Fact]
    internal void Constructor_NoStrategies_Throws()
    {
        _ = Assert.Throws<InvalidOperationException>(
            () => new StrategyRegistry<KeyDerivationAlgorithm, IKeyDerivationAlgorithmStrategy>([])
        );
    }

    /// <summary>
    /// Creates a substitute strategy that advertises the given identifier, which is all the registry
    /// indexes on.
    /// </summary>
    /// <typeparam name="TId">The enum naming the strategy family.</typeparam>
    /// <typeparam name="TStrategy">The strategy family to substitute.</typeparam>
    /// <param name="id">The identifier the stub reports.</param>
    /// <returns>A substitute strategy whose <see cref="IAlgorithmStrategy{TId}.Id"/> reports <paramref name="id"/>.</returns>
    private static TStrategy Stub<TId, TStrategy>(TId id)
        where TId : struct, Enum
        where TStrategy : class, IAlgorithmStrategy<TId>
    {
        var strategy = Substitute.For<TStrategy>();
        _ = strategy.Id.Returns(id);
        return strategy;
    }
}
