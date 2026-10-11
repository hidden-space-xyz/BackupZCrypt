using System.Collections.ObjectModel;

using BackupZCrypt.Application.Algorithms;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Test.Common;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

namespace BackupZCrypt.Test.Unit.Application;

/// <summary>
/// Unit tests for <see cref="AlgorithmCatalog"/>, the list of algorithms a front end offers for a new
/// archive.
/// </summary>
/// <remarks>
/// A front end shows these lists as they come, so the catalog owns the <see cref="CompressionMode.None"/>
/// entry no strategy implements, and it must offer exactly what the engine's registries resolve.
/// </remarks>
public sealed class AlgorithmCatalogTests
{
    [Fact]
    internal void RegisteredCatalog_OffersEveryAlgorithmInIdentifierOrder()
    {
        using var provider = TestHost.CreateProvider();

        var catalog = provider.GetRequiredService<IAlgorithmCatalog>();

        Assert.Multiple(
            () => Assert.Equal(Enum.GetValues<EncryptionAlgorithm>(), catalog.EncryptionAlgorithms),
            () => Assert.Equal(Enum.GetValues<KeyDerivationAlgorithm>(), catalog.KeyDerivationAlgorithms),
            () => Assert.Equal(Enum.GetValues<CompressionMode>(), catalog.CompressionModes)
        );
    }

    [Fact]
    internal void Catalog_OffersWhatTheRegistriesHoldWithNoCompressionInFront()
    {
        AlgorithmCatalog catalog = new(
            Registry<EncryptionAlgorithm, IEncryptionAlgorithmStrategy>(EncryptionAlgorithm.Aes, EncryptionAlgorithm.Serpent),
            Registry<KeyDerivationAlgorithm, IKeyDerivationAlgorithmStrategy>(KeyDerivationAlgorithm.Scrypt),
            Registry<CompressionMode, ICompressionStrategy>(CompressionMode.ZstdFast, CompressionMode.ZstdBest)
        );

        Assert.Multiple(
            () => Assert.Equal([EncryptionAlgorithm.Aes, EncryptionAlgorithm.Serpent], catalog.EncryptionAlgorithms),
            () => Assert.Equal([KeyDerivationAlgorithm.Scrypt], catalog.KeyDerivationAlgorithms),
            () =>
                Assert.Equal(
                    [CompressionMode.None, CompressionMode.ZstdFast, CompressionMode.ZstdBest],
                    catalog.CompressionModes
                )
        );
    }

    [Fact]
    internal void CompressionRegistryClaimingNoCompression_DoesNotListItTwice()
    {
        AlgorithmCatalog catalog = new(
            Registry<EncryptionAlgorithm, IEncryptionAlgorithmStrategy>(EncryptionAlgorithm.Aes),
            Registry<KeyDerivationAlgorithm, IKeyDerivationAlgorithmStrategy>(KeyDerivationAlgorithm.PBKDF2),
            Registry<CompressionMode, ICompressionStrategy>(CompressionMode.None, CompressionMode.Zstd)
        );

        Assert.Equal([CompressionMode.None, CompressionMode.Zstd], catalog.CompressionModes);
    }

    [Fact]
    internal void Lists_CannotBeChangedThroughTheSharedInstance()
    {
        using var provider = TestHost.CreateProvider();

        var catalog = provider.GetRequiredService<IAlgorithmCatalog>();

        Assert.Multiple(
            () => Assert.IsType<ReadOnlyCollection<EncryptionAlgorithm>>(catalog.EncryptionAlgorithms),
            () => Assert.IsType<ReadOnlyCollection<KeyDerivationAlgorithm>>(catalog.KeyDerivationAlgorithms),
            () => Assert.IsType<ReadOnlyCollection<CompressionMode>>(catalog.CompressionModes)
        );
    }

    [Fact]
    internal void MissingRegistry_IsRejected()
    {
        _ = Assert.Throws<ArgumentNullException>(
            () =>
                new AlgorithmCatalog(
                    null!,
                    Registry<KeyDerivationAlgorithm, IKeyDerivationAlgorithmStrategy>(KeyDerivationAlgorithm.PBKDF2),
                    Registry<CompressionMode, ICompressionStrategy>()
                )
        );
    }

    /// <summary>
    /// Creates a registry that reports the given identifiers as registered.
    /// </summary>
    /// <typeparam name="TId">The enum naming the family's implementations.</typeparam>
    /// <typeparam name="TStrategy">The strategy family.</typeparam>
    /// <param name="ids">The identifiers reported, in the order the registry would list them.</param>
    /// <returns>The substituted registry.</returns>
    private static IStrategyRegistry<TId, TStrategy> Registry<TId, TStrategy>(params TId[] ids)
        where TId : struct, Enum
        where TStrategy : IAlgorithmStrategy<TId>
    {
        var registry = Substitute.For<IStrategyRegistry<TId, TStrategy>>();
        _ = registry.Ids.Returns(Array.AsReadOnly(ids));

        return registry;
    }
}
