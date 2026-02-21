namespace BackupZCrypt.Domain.Factories.Interfaces;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;

public interface IEncryptionServiceFactory
{
    IEncryptionAlgorithmStrategy Create(EncryptionAlgorithm algorithm);
}
