using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Factories.Interfaces;
using BackupZCrypt.Domain.Strategies.Interfaces;

namespace BackupZCrypt.Domain.Factories;

/// <summary>
/// Resolves registered compression strategies by their compression mode.
/// </summary>
/// <param name="strategies">The registered compression strategies to resolve by mode.</param>
internal sealed class CompressionServiceFactory(IEnumerable<ICompressionStrategy> strategies)
    : ICompressionServiceFactory
{
    private readonly Dictionary<CompressionMode, ICompressionStrategy> strategies =
        strategies.ToDictionary(s => s.Id, s => s);

    /// <summary>
    /// Returns the strategy registered for the specified compression mode.
    /// </summary>
    /// <param name="mode">The compression mode to resolve.</param>
    /// <returns>The matching <see cref="ICompressionStrategy"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">No strategy is registered for <paramref name="mode"/>.</exception>
    public ICompressionStrategy Create(CompressionMode mode)
    {
        return !this.strategies.TryGetValue(mode, out var strategy)
            ? throw new ArgumentOutOfRangeException(
                nameof(mode),
                $"Compression mode '{mode}' is not registered."
            )
            : strategy;
    }
}
