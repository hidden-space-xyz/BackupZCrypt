using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Factories.Interfaces;
using BackupZCrypt.Domain.Strategies.Interfaces;

namespace BackupZCrypt.Domain.Factories;

/// <summary>
/// Resolves registered encryption strategies by their algorithm identifier.
/// </summary>
/// <param name="strategies">The registered encryption strategies to resolve by algorithm.</param>
internal sealed class EncryptionServiceFactory(IEnumerable<IEncryptionAlgorithmStrategy> strategies)
    : IEncryptionServiceFactory
{
    /// <summary>
    /// The lookup table of registered encryption strategies keyed by the algorithm each one implements.
    /// </summary>
    private readonly Dictionary<EncryptionAlgorithm, IEncryptionAlgorithmStrategy> strategies =
        strategies.ToDictionary(s => s.Id, s => s);

    /// <inheritdoc/>
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
