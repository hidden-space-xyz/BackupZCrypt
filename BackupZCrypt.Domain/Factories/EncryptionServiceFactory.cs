using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Factories.Interfaces;
using BackupZCrypt.Domain.Strategies.Interfaces;

namespace BackupZCrypt.Domain.Factories;

/// <summary>
/// Resolves registered encryption strategies by their algorithm identifier.
/// </summary>
internal sealed class EncryptionServiceFactory(IEnumerable<IEncryptionAlgorithmStrategy> strategies)
    : IEncryptionServiceFactory
{
    private readonly Dictionary<EncryptionAlgorithm, IEncryptionAlgorithmStrategy> strategies =
        strategies.ToDictionary(s => s.Id, s => s);

    /// <summary>
    /// Returns the strategy registered for the specified encryption algorithm.
    /// </summary>
    /// <param name="algorithm">The encryption algorithm to resolve.</param>
    /// <returns>The matching <see cref="IEncryptionAlgorithmStrategy"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">No strategy is registered for <paramref name="algorithm"/>.</exception>
    public IEncryptionAlgorithmStrategy Create(EncryptionAlgorithm algorithm)
    {
        return !this.strategies.TryGetValue(algorithm, out var strategy)
            ? throw new ArgumentOutOfRangeException(
                nameof(algorithm),
                $"Encryption algorithm '{algorithm}' is not registered."
            )
            : strategy;
    }
}
