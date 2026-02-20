namespace CloudZCrypt.Domain.Services.Interfaces;

using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.ValueObjects.Encryption;

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
