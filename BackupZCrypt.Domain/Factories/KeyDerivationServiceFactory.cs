using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Factories.Interfaces;
using BackupZCrypt.Domain.Strategies.Interfaces;

namespace BackupZCrypt.Domain.Factories;

/// <summary>
/// Resolves registered key derivation strategies by their algorithm identifier.
/// </summary>
/// <param name="strategies">The registered key derivation strategies to resolve by algorithm.</param>
internal sealed class KeyDerivationServiceFactory(
    IEnumerable<IKeyDerivationAlgorithmStrategy> strategies
) : IKeyDerivationServiceFactory
{
    /// <summary>
    /// The lookup table of registered key derivation strategies keyed by the algorithm each one implements.
    /// </summary>
    private readonly Dictionary<
        KeyDerivationAlgorithm,
        IKeyDerivationAlgorithmStrategy
    > strategies = strategies.ToDictionary(s => s.Id, s => s);

    /// <inheritdoc/>
    public IKeyDerivationAlgorithmStrategy Create(KeyDerivationAlgorithm algorithm)
    {
        return !this.strategies.TryGetValue(algorithm, out var strategy)
            ? throw new ArgumentOutOfRangeException(
                nameof(algorithm),
                $"Key derivation algorithm '{algorithm}' is not registered."
            )
            : strategy;
    }
}
