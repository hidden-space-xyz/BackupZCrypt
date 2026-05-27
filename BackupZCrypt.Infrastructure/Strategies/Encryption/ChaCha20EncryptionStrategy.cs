using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Infrastructure.Resources;

using System.Security.Cryptography;

namespace BackupZCrypt.Infrastructure.Strategies.Encryption;

internal sealed class ChaCha20EncryptionStrategy : IEncryptionAlgorithmStrategy
{
    public EncryptionAlgorithm Id => EncryptionAlgorithm.ChaCha20;

    public string DisplayName => Messages.ChaCha20DisplayName;

    public string Description => Messages.ChaCha20Description;

    public string Summary => Messages.ChaCha20Summary;

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
