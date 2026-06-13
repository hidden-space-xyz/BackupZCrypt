using BackupZCrypt.Domain.Enums;

namespace BackupZCrypt.Domain.Strategies.Interfaces;

public interface IKeyDerivationAlgorithmStrategy
{
    KeyDerivationAlgorithm Id { get; }

    byte[] DeriveKey(string password, byte[] salt, int keySize);
}
