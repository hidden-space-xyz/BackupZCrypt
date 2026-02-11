using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Factories.Interfaces;
using CloudZCrypt.Domain.Resources;
using CloudZCrypt.Domain.Strategies.Interfaces;

namespace CloudZCrypt.Domain.Factories;

internal class EncryptionServiceFactory(IEnumerable<IEncryptionAlgorithmStrategy> strategies)
    : IEncryptionServiceFactory
{
    private readonly IReadOnlyDictionary<
        EncryptionAlgorithm,
        IEncryptionAlgorithmStrategy
    > strategies = strategies.ToDictionary(s => s.Id, s => s);

    public IEncryptionAlgorithmStrategy Create(EncryptionAlgorithm algorithm)
    {
        return !strategies.TryGetValue(algorithm, out IEncryptionAlgorithmStrategy? strategy)
            ? throw new ArgumentOutOfRangeException(
                nameof(algorithm),
                string.Format(Messages.EncryptionAlgorithmNotRegisteredFormat, algorithm)
            )
            : strategy;
    }
}
