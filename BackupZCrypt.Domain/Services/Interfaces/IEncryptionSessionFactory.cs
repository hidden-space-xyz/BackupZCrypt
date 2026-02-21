namespace BackupZCrypt.Domain.Services.Interfaces;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.ValueObjects.Encryption;

public interface IEncryptionSessionFactory
{
    EncryptionSession CreateEncryptionSession(
        string password,
        KeyDerivationAlgorithm keyDerivationAlgorithm,
        CompressionMode compression);

    Task<EncryptionSession> CreateDecryptionSessionAsync(
        Stream source,
        string password,
        KeyDerivationAlgorithm keyDerivationAlgorithm,
        CancellationToken cancellationToken);
}
