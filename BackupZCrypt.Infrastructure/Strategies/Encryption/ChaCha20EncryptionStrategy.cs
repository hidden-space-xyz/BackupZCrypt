using System.Security.Cryptography;
using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;

namespace BackupZCrypt.Infrastructure.Strategies.Encryption;

internal sealed class ChaCha20EncryptionStrategy : IEncryptionAlgorithmStrategy
{
    public EncryptionAlgorithm Id => EncryptionAlgorithm.ChaCha20;

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
            using ChaCha20Poly1305 cipher = new(key);
            cipher.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);

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
            using ChaCha20Poly1305 cipher = new(key);
            cipher.Decrypt(nonce, ciphertext[..dataLength], tag, plaintext, associatedData);
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
