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
    /// <summary>
    /// The lookup table of registered compression strategies keyed by the mode each one implements.
    /// </summary>
    private readonly Dictionary<CompressionMode, ICompressionStrategy> strategies =
        strategies.ToDictionary(s => s.Id, s => s);

    /// <inheritdoc/>
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
