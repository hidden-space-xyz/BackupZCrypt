namespace CloudZCrypt.Domain.Factories.Interfaces;

using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Strategies.Interfaces;

public interface IEncryptionServiceFactory
{
    IEncryptionAlgorithmStrategy Create(EncryptionAlgorithm algorithm);
}
