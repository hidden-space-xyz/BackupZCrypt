namespace BackupZCrypt.Domain.Strategies.Interfaces;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.ValueObjects.Encryption;

public interface IEncryptionAlgorithmStrategy
{
    EncryptionAlgorithm Id { get; }

    string DisplayName { get; }

    string Description { get; }

    string Summary { get; }

    Task<EncryptionMetadata> EncryptFileAsync(
        string sourceFilePath,
        string destinationFilePath,
        string password,
        KeyDerivationAlgorithm keyDerivationAlgorithm,
        CompressionMode compression = CompressionMode.None,
        CancellationToken cancellationToken = default);

    Task<bool> DecryptFileAsync(
        string sourceFilePath,
        string destinationFilePath,
        string password,
        KeyDerivationAlgorithm keyDerivationAlgorithm,
        EncryptionMetadata metadata,
        CancellationToken cancellationToken = default);

    Task<bool> DecryptFileAsync(
        string sourceFilePath,
        string destinationFilePath,
        string password,
        KeyDerivationAlgorithm keyDerivationAlgorithm,
        CancellationToken cancellationToken = default);

    Task<bool> CreateEncryptedFileAsync(
        byte[] plaintextData,
        string destinationFilePath,
        string password,
        KeyDerivationAlgorithm keyDerivationAlgorithm,
        CompressionMode compression = CompressionMode.None,
        CancellationToken cancellationToken = default);

    Task<byte[]> CreateEncryptedDataAsync(
        byte[] plaintextData,
        string password,
        KeyDerivationAlgorithm keyDerivationAlgorithm,
        CompressionMode compression = CompressionMode.None,
        CancellationToken cancellationToken = default);

    Task<byte[]> ReadEncryptedFileAsync(
        string sourceFilePath,
        string password,
        KeyDerivationAlgorithm keyDerivationAlgorithm,
        CancellationToken cancellationToken = default);

    Task<byte[]> ReadEncryptedDataAsync(
        ReadOnlyMemory<byte> encryptedData,
        string password,
        KeyDerivationAlgorithm keyDerivationAlgorithm,
        CancellationToken cancellationToken = default);
}
