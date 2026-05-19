namespace BackupZCrypt.Infrastructure.Strategies.Encryption.Algorithms;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Infrastructure.Resources;

internal sealed class TwofishEncryptionStrategy : IEncryptionAlgorithmStrategy
{
    public EncryptionAlgorithm Id => EncryptionAlgorithm.Twofish;

    public string DisplayName => Messages.TwofishDisplayName;

    public string Description => Messages.TwofishDescription;

    public string Summary => Messages.TwofishSummary;
}
