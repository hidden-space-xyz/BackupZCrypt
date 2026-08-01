using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Factories;
using BackupZCrypt.Domain.Factories.Interfaces;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Test.Common;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

namespace BackupZCrypt.Test.Unit.Composition;

/// <summary>
/// Guards the composition root against a missing strategy registration. Adding an algorithm means
/// adding an enum member, implementing the strategy, and registering it in
/// <c>DependencyInjection.AddBackupZCryptServices</c>; forgetting the last step is otherwise invisible
/// until a user has already typed a password, and for a restore it would mean an unreadable backup.
/// The cases are driven from <see cref="Enum.GetValues{TEnum}"/>, so a new enum member fails here
/// until it is wired up.
/// </summary>
/// <remarks>
/// The negative cases pin the other half of that contract: a factory asked for an identifier it has no
/// strategy for must throw rather than silently fall back to whichever strategy happens to be
/// registered. Encrypting with an algorithm the manifest does not name would make the archive
/// undecryptable.
/// </remarks>
public sealed class StrategyRegistrationTests
{
    /// <summary>
    /// Supplies every declared encryption algorithm, so a new member automatically becomes a case.
    /// </summary>
    /// <returns>Every value of <see cref="EncryptionAlgorithm"/>.</returns>
    private static IEnumerable<EncryptionAlgorithm> EncryptionAlgorithms() =>
        Enum.GetValues<EncryptionAlgorithm>();

    /// <summary>
    /// Supplies every declared key derivation algorithm, so a new member automatically becomes a case.
    /// </summary>
    /// <returns>Every value of <see cref="KeyDerivationAlgorithm"/>.</returns>
    private static IEnumerable<KeyDerivationAlgorithm> KeyDerivationAlgorithms() =>
        Enum.GetValues<KeyDerivationAlgorithm>();

    /// <summary>
    /// Supplies every compression mode that is expected to resolve to a strategy.
    /// <see cref="CompressionMode.None"/> is excluded on purpose: it is deliberately unregistered
    /// and is covered by <see cref="Create_CompressionModeNone_IsDeliberatelyUnregistered"/>.
    /// </summary>
    /// <remarks>
    /// <c>ChunkedBackupService</c> and <c>BackupBenchmarkService</c> map <see cref="CompressionMode.None"/>
    /// to a null strategy before ever reaching the factory, so registering a pass-through strategy for it
    /// would silently change the on-disk chunk layout. Leaving it unregistered keeps that mapping the
    /// callers' job.
    /// </remarks>
    /// <returns>Every value of <see cref="CompressionMode"/> except <see cref="CompressionMode.None"/>.</returns>
    private static IEnumerable<CompressionMode> CompressionModes() =>
        Enum.GetValues<CompressionMode>().Where(mode => mode != CompressionMode.None);

    [TestCaseSource(nameof(EncryptionAlgorithms))]
    public void Create_EveryEncryptionAlgorithm_ResolvesStrategyWithMatchingId(
        EncryptionAlgorithm algorithm
    )
    {
        using var provider = TestHost.CreateProvider();
        var factory = provider.GetRequiredService<IEncryptionServiceFactory>();

        var strategy = factory.Create(algorithm);

        Assert.That(
            strategy.Id,
            Is.EqualTo(algorithm),
            $"The container resolved '{strategy.GetType().Name}' for {algorithm}."
        );
    }

    [TestCaseSource(nameof(KeyDerivationAlgorithms))]
    public void Create_EveryKeyDerivationAlgorithm_ResolvesStrategyWithMatchingId(
        KeyDerivationAlgorithm algorithm
    )
    {
        using var provider = TestHost.CreateProvider();
        var factory = provider.GetRequiredService<IKeyDerivationServiceFactory>();

        var strategy = factory.Create(algorithm);

        Assert.That(
            strategy.Id,
            Is.EqualTo(algorithm),
            $"The container resolved '{strategy.GetType().Name}' for {algorithm}."
        );
    }

    [TestCaseSource(nameof(CompressionModes))]
    public void Create_EveryCompressionMode_ResolvesStrategyWithMatchingId(CompressionMode mode)
    {
        using var provider = TestHost.CreateProvider();
        var factory = provider.GetRequiredService<ICompressionServiceFactory>();

        var strategy = factory.Create(mode);

        Assert.That(
            strategy.Id,
            Is.EqualTo(mode),
            $"The container resolved '{strategy.GetType().Name}' for {mode}."
        );
    }

    [Test]
    public void Create_CompressionModeNone_IsDeliberatelyUnregistered()
    {
        using var provider = TestHost.CreateProvider();
        var factory = provider.GetRequiredService<ICompressionServiceFactory>();

        _ = Assert.Throws<ArgumentOutOfRangeException>(() => factory.Create(CompressionMode.None));
    }

    [Test]
    public void Registrations_ContainExactlyOneChunkingStrategy()
    {
        using var provider = TestHost.CreateProvider();

        Assert.That(
            provider.GetServices<IChunkingStrategy>().ToList(),
            Has.Count.EqualTo(1),
            "Chunking is the one strategy family with no enum identifier and no manifest field "
                + "recording which implementation ran. A second registration would change chunk "
                + "boundaries with nothing on disk to say so, destroying deduplication against every "
                + "archive already written."
        );
    }

    [Test]
    public void Create_UnregisteredEncryptionAlgorithm_ThrowsInsteadOfSubstitutingAnotherCipher()
    {
        var registered = Substitute.For<IEncryptionAlgorithmStrategy>();
        _ = registered.Id.Returns(EncryptionAlgorithm.Aes);
        var factory = new EncryptionServiceFactory([registered]);

        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => factory.Create(EncryptionAlgorithm.Serpent)
        );
    }
}
