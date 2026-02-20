namespace CloudZCrypt.Domain.Strategies.Interfaces;

using CloudZCrypt.Domain.Enums;

public interface IKeyDerivationAlgorithmStrategy
{
    KeyDerivationAlgorithm Id { get; }

    string DisplayName { get; }

    string Description { get; }

    string Summary { get; }

    byte[] DeriveKey(string password, byte[] salt, int keySize);
}
