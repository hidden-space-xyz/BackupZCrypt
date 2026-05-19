namespace BackupZCrypt.Infrastructure.Strategies.Encryption.Algorithms;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Infrastructure.Resources;

internal sealed class SerpentEncryptionStrategy : IEncryptionAlgorithmStrategy
{
    public EncryptionAlgorithm Id => EncryptionAlgorithm.Serpent;

    public string DisplayName => Messages.SerpentDisplayName;

    public string Description => Messages.SerpentDescription;

    public string Summary => Messages.SerpentSummary;
}
