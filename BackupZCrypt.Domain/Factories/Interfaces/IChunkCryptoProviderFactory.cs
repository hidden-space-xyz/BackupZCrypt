namespace BackupZCrypt.Domain.Factories.Interfaces;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;

public interface IChunkCryptoProviderFactory
{
    IChunkCryptoProvider Create(EncryptionAlgorithm algorithm);
}
