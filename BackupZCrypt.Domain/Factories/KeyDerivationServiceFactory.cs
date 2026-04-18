namespace BackupZCrypt.Domain.Factories;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Factories.Interfaces;
using BackupZCrypt.Domain.Resources;
using BackupZCrypt.Domain.Strategies.Interfaces;

internal sealed class KeyDerivationServiceFactory(IEnumerable<IKeyDerivationAlgorithmStrategy> strategies)
    : IKeyDerivationServiceFactory
{
    private readonly Dictionary<
        KeyDerivationAlgorithm,
        IKeyDerivationAlgorithmStrategy
    > strategies = strategies.ToDictionary(s => s.Id, s => s);

    public IKeyDerivationAlgorithmStrategy Create(KeyDerivationAlgorithm algorithm)
    {
        return !this.strategies.TryGetValue(algorithm, out IKeyDerivationAlgorithmStrategy? strategy)
            ? throw new ArgumentOutOfRangeException(
                nameof(algorithm),
                string.Format(Messages.KeyDerivationAlgorithmNotRegisteredFormat, algorithm))
            : strategy;
    }
}
