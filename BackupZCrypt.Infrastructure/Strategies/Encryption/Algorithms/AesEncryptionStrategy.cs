namespace BackupZCrypt.Infrastructure.Strategies.Encryption.Algorithms;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Factories.Interfaces;
using BackupZCrypt.Domain.Services.Interfaces;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Infrastructure.Resources;
using System.Security.Cryptography;

internal class AesEncryptionStrategy(
    IEncryptionSessionFactory encryptionSessionFactory,
    ICompressionServiceFactory compressionServiceFactory,
    IEncryptionFileService encryptionFileService)
    : EncryptionStrategyBase(
        encryptionSessionFactory,
        compressionServiceFactory,
        encryptionFileService),
        IEncryptionAlgorithmStrategy
{
    public EncryptionAlgorithm Id => EncryptionAlgorithm.Aes;

    public string DisplayName => Messages.AesDisplayName;

    public string Description => Messages.AesDescription;

    public string Summary => Messages.AesSummary;

    protected override async Task EncryptStreamAsync(
        Stream sourceStream,
        Stream destinationStream,
        byte[] key,
        byte[] nonce,
        byte[] associatedData,
        CancellationToken cancellationToken)
    {
        byte[] plaintext = await ReadAllBytesAsync(sourceStream, cancellationToken);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[MacSizeBytes];

        try
        {
            using AesGcm aesGcm = new(key, MacSizeBytes);
            aesGcm.Encrypt(
                nonce,
                plaintext,
                ciphertext,
                tag,
                associatedData);

            await destinationStream.WriteAsync(ciphertext, cancellationToken);
            await destinationStream.WriteAsync(tag, cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(tag);
        }
    }

    protected override async Task DecryptStreamAsync(
        Stream sourceStream,
        Stream destinationStream,
        byte[] key,
        byte[] nonce,
        byte[] associatedData,
        CancellationToken cancellationToken)
    {
        byte[] encryptedData = await ReadAllBytesAsync(sourceStream, cancellationToken);
        if (encryptedData.Length < MacSizeBytes)
        {
            throw new CryptographicException();
        }

        byte[] plaintext = new byte[encryptedData.Length - MacSizeBytes];
        byte[] tag = new byte[MacSizeBytes];

        try
        {
            encryptedData.AsSpan(plaintext.Length, MacSizeBytes).CopyTo(tag);

            using AesGcm aesGcm = new(key, MacSizeBytes);
            aesGcm.Decrypt(
                nonce,
                encryptedData.AsSpan(0, plaintext.Length),
                tag,
                plaintext,
                associatedData);

            await destinationStream.WriteAsync(plaintext, cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encryptedData);
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(tag);
        }
    }
}
