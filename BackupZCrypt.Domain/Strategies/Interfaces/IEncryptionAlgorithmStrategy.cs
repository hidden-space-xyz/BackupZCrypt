using BackupZCrypt.Domain.Enums;

namespace BackupZCrypt.Domain.Strategies.Interfaces;

/// <summary>
/// Encrypts and decrypts individual chunks using a specific AEAD cipher.
/// </summary>
public interface IEncryptionAlgorithmStrategy
{
    /// <summary>
    /// Gets the algorithm this strategy implements, used to select it by enum value.
    /// </summary>
    public EncryptionAlgorithm Id { get; }

    /// <summary>
    /// Encrypts a chunk and appends the authentication tag to the ciphertext.
    /// </summary>
    /// <param name="plaintext">The chunk data to encrypt.</param>
    /// <param name="key">The symmetric encryption key.</param>
    /// <param name="nonce">The unique nonce for this chunk.</param>
    /// <param name="associatedData">The additional authenticated data bound to the ciphertext.</param>
    /// <returns>The ciphertext with the appended authentication tag.</returns>
    public byte[] EncryptChunk(
        ReadOnlySpan<byte> plaintext,
        byte[] key,
        byte[] nonce,
        byte[] associatedData
    );

    /// <summary>
    /// Verifies the authentication tag and decrypts a chunk.
    /// </summary>
    /// <param name="ciphertext">The ciphertext with its appended authentication tag.</param>
    /// <param name="key">The symmetric encryption key.</param>
    /// <param name="nonce">The nonce that was used to encrypt the chunk.</param>
    /// <param name="associatedData">The additional authenticated data that was bound to the ciphertext.</param>
    /// <returns>The recovered plaintext.</returns>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// The ciphertext is shorter than the authentication tag, or tag verification fails because the
    /// ciphertext, nonce, or associated data was tampered with or the key is wrong.
    /// </exception>
    public byte[] DecryptChunk(
        ReadOnlySpan<byte> ciphertext,
        byte[] key,
        byte[] nonce,
        byte[] associatedData
    );
}
