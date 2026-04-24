namespace BackupZCrypt.Domain.Factories;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Factories.Interfaces;
using BackupZCrypt.Domain.Resources;
using BackupZCrypt.Domain.Strategies.Interfaces;

internal sealed class EncryptionServiceFactory(IEnumerable<IEncryptionAlgorithmStrategy> strategies)
    : IEncryptionServiceFactory
{
    private readonly Dictionary<
        EncryptionAlgorithm,
        IEncryptionAlgorithmStrategy
    > strategies = strategies.ToDictionary(s => s.Id, s => s);

    public IEncryptionAlgorithmStrategy Create(EncryptionAlgorithm algorithm)
    {
        return !this.strategies.TryGetValue(algorithm, out var strategy)
            ? throw new ArgumentOutOfRangeException(
                nameof(algorithm),
                string.Format(Messages.EncryptionAlgorithmNotRegisteredFormat, algorithm))
            : strategy;
    }
}
