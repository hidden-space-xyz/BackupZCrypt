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
    private readonly Dictionary<
        KeyDerivationAlgorithm,
        IKeyDerivationAlgorithmStrategy
    > strategies = strategies.ToDictionary(s => s.Id, s => s);

    /// <summary>
    /// Returns the strategy registered for the specified key derivation algorithm.
    /// </summary>
    /// <param name="algorithm">The key derivation algorithm to resolve.</param>
    /// <returns>The matching <see cref="IKeyDerivationAlgorithmStrategy"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">No strategy is registered for <paramref name="algorithm"/>.</exception>
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
