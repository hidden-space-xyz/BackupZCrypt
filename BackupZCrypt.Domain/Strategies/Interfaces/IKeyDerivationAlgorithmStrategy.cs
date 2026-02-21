namespace BackupZCrypt.Domain.Strategies.Interfaces;

using BackupZCrypt.Domain.Enums;

public interface IKeyDerivationAlgorithmStrategy
{
    KeyDerivationAlgorithm Id { get; }

    string DisplayName { get; }

    string Description { get; }

    string Summary { get; }

    byte[] DeriveKey(string password, byte[] salt, int keySize);
}
