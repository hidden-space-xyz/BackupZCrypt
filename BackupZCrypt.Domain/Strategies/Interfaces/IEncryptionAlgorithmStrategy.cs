namespace BackupZCrypt.Domain.Strategies.Interfaces;

using BackupZCrypt.Domain.Enums;

public interface IEncryptionAlgorithmStrategy
{
    EncryptionAlgorithm Id { get; }

    string DisplayName { get; }

    string Description { get; }

    string Summary { get; }
}
