using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Infrastructure.Resources;

using System.Security.Cryptography;

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
        byte[] associatedData)
    {
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[EncryptionConstants.MacSize / 8];

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
        byte[] associatedData)
    {
        const int tagSize = EncryptionConstants.MacSize / 8;
        if (ciphertext.Length < tagSize)
        {
            throw new CryptographicException();
        }

        var dataLength = ciphertext.Length - tagSize;
        var plaintext = new byte[dataLength];
        var tag = ciphertext[dataLength..].ToArray();

        try
        {
            using AesGcm aes = new(key, tagSize);
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
