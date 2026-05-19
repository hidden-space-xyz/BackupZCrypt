namespace BackupZCrypt.Infrastructure.Strategies.Encryption.Algorithms;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Infrastructure.Resources;

internal sealed class CamelliaEncryptionStrategy : IEncryptionAlgorithmStrategy
{
    public EncryptionAlgorithm Id => EncryptionAlgorithm.Camellia;

    public string DisplayName => Messages.CamelliaDisplayName;

    public string Description => Messages.CamelliaDescription;

    public string Summary => Messages.CamelliaSummary;
}
