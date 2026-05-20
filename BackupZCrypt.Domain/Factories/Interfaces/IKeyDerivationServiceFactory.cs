using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;

namespace BackupZCrypt.Domain.Factories.Interfaces;

public interface IKeyDerivationServiceFactory
{
    IKeyDerivationAlgorithmStrategy Create(KeyDerivationAlgorithm algorithm);
}
