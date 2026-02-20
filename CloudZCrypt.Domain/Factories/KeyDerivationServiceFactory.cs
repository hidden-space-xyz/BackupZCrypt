namespace CloudZCrypt.Domain.Factories;

using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Factories.Interfaces;
using CloudZCrypt.Domain.Resources;
using CloudZCrypt.Domain.Strategies.Interfaces;

internal class KeyDerivationServiceFactory(IEnumerable<IKeyDerivationAlgorithmStrategy> strategies)
    : IKeyDerivationServiceFactory
{
    private readonly IReadOnlyDictionary<
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
