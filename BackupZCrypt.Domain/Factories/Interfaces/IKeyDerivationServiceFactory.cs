using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;

namespace BackupZCrypt.Domain.Factories.Interfaces;

/// <summary>
/// Resolves the key derivation strategy that implements a given algorithm.
/// </summary>
public interface IKeyDerivationServiceFactory
{
    /// <summary>
    /// Returns the strategy registered for the specified key derivation algorithm.
    /// </summary>
    /// <param name="algorithm">The key derivation algorithm to resolve.</param>
    /// <returns>The matching <see cref="IKeyDerivationAlgorithmStrategy"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">No strategy is registered for <paramref name="algorithm"/>.</exception>
    public IKeyDerivationAlgorithmStrategy Create(KeyDerivationAlgorithm algorithm);
}
