namespace BackupZCrypt.Infrastructure.Strategies.Encryption.Algorithms;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Factories.Interfaces;
using BackupZCrypt.Domain.Services.Interfaces;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Infrastructure.Resources;
using System.Security.Cryptography;

internal sealed class ChaCha20EncryptionStrategy(
    IEncryptionSessionFactory encryptionSessionFactory,
    ICompressionServiceFactory compressionServiceFactory,
    IEncryptionFileService encryptionFileService)
    : EncryptionStrategyBase(
        encryptionSessionFactory,
        compressionServiceFactory,
        encryptionFileService),
        IEncryptionAlgorithmStrategy
{
    public EncryptionAlgorithm Id => EncryptionAlgorithm.ChaCha20;

    public string DisplayName => Messages.ChaCha20DisplayName;

    public string Description => Messages.ChaCha20Description;

    public string Summary => Messages.ChaCha20Summary;

    protected override async Task EncryptStreamAsync(
        Stream sourceStream,
        Stream destinationStream,
        byte[] key,
        byte[] nonce,
        byte[] associatedData,
        CancellationToken cancellationToken)
    {
        var plaintext = await ReadAllBytesAsync(sourceStream, cancellationToken);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[MacSizeBytes];

        try
        {
            using ChaCha20Poly1305 chaCha20Poly1305 = new(key);
            chaCha20Poly1305.Encrypt(
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
        var encryptedData = await ReadAllBytesAsync(sourceStream, cancellationToken);
        if (encryptedData.Length < MacSizeBytes)
        {
            throw new CryptographicException();
        }

        var plaintext = new byte[encryptedData.Length - MacSizeBytes];
        var tag = new byte[MacSizeBytes];

        try
        {
            encryptedData.AsSpan(plaintext.Length, MacSizeBytes).CopyTo(tag);

            using ChaCha20Poly1305 chaCha20Poly1305 = new(key);
            chaCha20Poly1305.Decrypt(
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
