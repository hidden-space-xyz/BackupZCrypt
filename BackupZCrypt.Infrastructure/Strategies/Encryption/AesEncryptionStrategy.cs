using System.Security.Cryptography;
using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Infrastructure.Resources;

namespace BackupZCrypt.Infrastructure.Strategies.Encryption;

internal sealed class AesEncryptionStrategy : IEncryptionAlgorithmStrategy
{
    public EncryptionAlgorithm Id => EncryptionAlgorithm.Aes;

    public string DisplayName => Messages.AesDisplayName;

    public string Description => Messages.AesDescription;

    public string Summary => Messages.AesSummary;

    public byte[] EncryptChunk(
        ReadOnlySpan<byte> plaintext,
        byte[] key,
        byte[] nonce,
        byte[] associatedData
    )
    {
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[EncryptionConstants.TagSize];

        try
        {
            using AesGcm aes = new(key, tag.Length);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);

            var result = new byte[ciphertext.Length + tag.Length];
            ciphertext.CopyTo(result.AsSpan());
            tag.CopyTo(result.AsSpan(ciphertext.Length));
            return result;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(tag);
        }
    }

    public byte[] DecryptChunk(
        ReadOnlySpan<byte> ciphertext,
        byte[] key,
        byte[] nonce,
        byte[] associatedData
    )
    {
        if (ciphertext.Length < EncryptionConstants.TagSize)
        {
            throw new CryptographicException();
        }

        var dataLength = ciphertext.Length - EncryptionConstants.TagSize;
        var plaintext = new byte[dataLength];
        var tag = ciphertext[dataLength..].ToArray();

        try
        {
            using AesGcm aes = new(key, EncryptionConstants.TagSize);
            aes.Decrypt(nonce, ciphertext[..dataLength], tag, plaintext, associatedData);
            return plaintext;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(tag);
        }
    }
}
