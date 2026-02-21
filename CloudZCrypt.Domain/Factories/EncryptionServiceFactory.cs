namespace CloudZCrypt.Domain.Factories;

using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Factories.Interfaces;
using CloudZCrypt.Domain.Resources;
using CloudZCrypt.Domain.Strategies.Interfaces;

internal class EncryptionServiceFactory(IEnumerable<IEncryptionAlgorithmStrategy> strategies)
    : IEncryptionServiceFactory
{
    private readonly Dictionary<
        EncryptionAlgorithm,
        IEncryptionAlgorithmStrategy
    > strategies = strategies.ToDictionary(s => s.Id, s => s);

    public IEncryptionAlgorithmStrategy Create(EncryptionAlgorithm algorithm)
    {
        return !this.strategies.TryGetValue(algorithm, out IEncryptionAlgorithmStrategy? strategy)
            ? throw new ArgumentOutOfRangeException(
                nameof(algorithm),
                string.Format(Messages.EncryptionAlgorithmNotRegisteredFormat, algorithm))
            : strategy;
    }
}
