using System.Security.Cryptography;
using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;

namespace BackupZCrypt.Infrastructure.Strategies.Encryption;

/// <summary>
/// AEAD encryption strategy using AES in Galois/Counter Mode (AES-GCM) via the platform
/// <see cref="AesGcm"/> primitive. The authentication tag is appended to the ciphertext,
/// and intermediate plaintext/tag buffers are zeroed once the result is assembled.
/// </summary>
internal sealed class AesEncryptionStrategy : IEncryptionAlgorithmStrategy
{
    /// <summary>
    /// Gets the algorithm identifier (<see cref="EncryptionAlgorithm.Aes"/>) used to select this strategy.
    /// </summary>
    public EncryptionAlgorithm Id => EncryptionAlgorithm.Aes;

    /// <summary>
    /// Encrypts a single chunk with AES-GCM and returns the ciphertext with the authentication
    /// tag appended.
    /// </summary>
    /// <param name="plaintext">The chunk to encrypt.</param>
    /// <param name="key">The encryption key.</param>
    /// <param name="nonce">The unique per-chunk nonce.</param>
    /// <param name="associatedData">Additional authenticated data bound to the ciphertext but not encrypted.</param>
    /// <returns>The ciphertext followed by the authentication tag.</returns>
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

    /// <summary>
    /// Verifies the appended authentication tag and decrypts a single AES-GCM chunk. On any
    /// failure the plaintext buffer is zeroed before the exception propagates.
    /// </summary>
    /// <param name="ciphertext">The ciphertext with the authentication tag appended.</param>
    /// <param name="key">The encryption key.</param>
    /// <param name="nonce">The nonce used during encryption.</param>
    /// <param name="associatedData">The additional authenticated data supplied during encryption.</param>
    /// <returns>The recovered plaintext.</returns>
    /// <exception cref="CryptographicException">
    /// The ciphertext is shorter than the tag, or authentication/decryption fails (tampering or wrong key).
    /// </exception>
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
