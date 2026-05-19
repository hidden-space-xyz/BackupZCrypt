namespace BackupZCrypt.Domain.Factories;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Factories.Interfaces;
using BackupZCrypt.Domain.Resources;
using BackupZCrypt.Domain.Strategies.Interfaces;

internal sealed class ChunkCryptoProviderFactory(IEnumerable<IChunkCryptoProvider> providers)
    : IChunkCryptoProviderFactory
{
    private readonly Dictionary<EncryptionAlgorithm, IChunkCryptoProvider> providers =
        providers.ToDictionary(p => p.Id, p => p);

    public IChunkCryptoProvider Create(EncryptionAlgorithm algorithm)
    {
        return !this.providers.TryGetValue(algorithm, out var provider)
            ? throw new ArgumentOutOfRangeException(
                nameof(algorithm),
                string.Format(Messages.EncryptionAlgorithmNotRegisteredFormat, algorithm))
            : provider;
    }
}
