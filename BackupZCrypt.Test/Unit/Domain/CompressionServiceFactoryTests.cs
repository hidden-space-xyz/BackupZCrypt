using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Factories;
using BackupZCrypt.Domain.Strategies.Interfaces;

using NSubstitute;

namespace BackupZCrypt.Test.Unit.Domain;

/// <summary>
/// Unit tests for the compression service factory's strategy resolution. The successful lookups are
/// covered end to end against the real container by
/// <see cref="BackupZCrypt.Test.Unit.Composition.StrategyRegistrationTests"/>; what only a
/// substituted strategy set can show is what the factory does when the requested mode has no
/// strategy at all.
/// </summary>
/// <remarks>
/// Falling back to whatever single strategy happens to be registered would compress chunks in a mode the
/// manifest does not record, so an unknown mode has to fail instead.
/// </remarks>
public sealed class CompressionServiceFactoryTests
{
    /// <summary>
    /// Creates a substitute strategy that advertises the given mode, which is all the factory
    /// indexes on.
    /// </summary>
    /// <param name="id">The compression mode the stub reports as its identifier.</param>
    /// <returns>A substitute strategy whose <see cref="ICompressionStrategy.Id"/> reports <paramref name="id"/>.</returns>
    private static ICompressionStrategy Stub(CompressionMode id)
    {
        var strategy = Substitute.For<ICompressionStrategy>();
        _ = strategy.Id.Returns(id);
        return strategy;
    }

    [Test]
    public void Create_UnregisteredMode_Throws()
    {
        var factory = new CompressionServiceFactory([Stub(CompressionMode.None)]);

        _ = Assert.Throws<ArgumentOutOfRangeException>(() => factory.Create(CompressionMode.ZstdBest));
    }
}
