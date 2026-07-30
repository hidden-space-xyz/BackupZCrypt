using System.Security.Cryptography;

using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;

namespace BackupZCrypt.Infrastructure.Strategies.Encryption;

/// <summary>
/// Base class for AEAD encryption strategies backed by a platform (BCL) AEAD primitive such as
/// AES-GCM or ChaCha20-Poly1305. Handles the shared flow: the ciphertext and its appended
/// authentication tag are produced directly into a single result buffer, that buffer is zeroed if
/// the primitive throws, and inputs too short to contain a tag are rejected before decryption is
/// attempted. Derived types supply the concrete primitive via <see cref="EncryptCore"/> and
/// <see cref="DecryptCore"/>.
/// </summary>
internal abstract class PlatformAeadEncryptionStrategyBase : IEncryptionAlgorithmStrategy
{
    /// <summary>
    /// Gets the algorithm identifier used to select the concrete strategy.
    /// </summary>
    public abstract EncryptionAlgorithm Id { get; }

    /// <summary>
    /// Encrypts a single chunk with the derived type's AEAD primitive and returns the ciphertext
    /// with the authentication tag appended.
    /// </summary>
    /// <param name="plaintext">The chunk to encrypt.</param>
    /// <param name="key">The encryption key.</param>
    /// <param name="nonce">The unique per-chunk nonce.</param>
    /// <param name="associatedData">The additional authenticated data bound to the ciphertext but not encrypted.</param>
    /// <returns>The ciphertext followed by the authentication tag.</returns>
    public byte[] EncryptChunk(
        ReadOnlySpan<byte> plaintext,
        byte[] key,
        byte[] nonce,
        byte[] associatedData
    )
    {
        var result = new byte[plaintext.Length + EncryptionConstants.TagSize];

        try
        {
            EncryptCore(
                key,
                nonce,
                plaintext,
                result.AsSpan(0, plaintext.Length),
                result.AsSpan(plaintext.Length),
                associatedData
            );

            return result;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(result);
            throw;
        }
    }

    /// <summary>
    /// Verifies the appended authentication tag and decrypts a single chunk with the derived type's
    /// AEAD primitive. On any failure the plaintext buffer is zeroed before the exception propagates.
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

        try
        {
            DecryptCore(
                key,
                nonce,
                ciphertext[..dataLength],
                ciphertext[dataLength..],
                plaintext,
                associatedData
            );

            return plaintext;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw;
        }
    }

    /// <summary>
    /// Encrypts <paramref name="plaintext"/> into <paramref name="ciphertext"/> and writes the
    /// authentication tag into <paramref name="tag"/> using the concrete AEAD primitive.
    /// </summary>
    /// <param name="key">The encryption key.</param>
    /// <param name="nonce">The unique per-chunk nonce.</param>
    /// <param name="plaintext">The chunk to encrypt.</param>
    /// <param name="ciphertext">The span that receives the ciphertext; the same length as <paramref name="plaintext"/>.</param>
    /// <param name="tag">The span that receives the authentication tag.</param>
    /// <param name="associatedData">The additional authenticated data bound to the ciphertext but not encrypted.</param>
    protected abstract void EncryptCore(
        byte[] key,
        byte[] nonce,
        ReadOnlySpan<byte> plaintext,
        Span<byte> ciphertext,
        Span<byte> tag,
        byte[] associatedData
    );

    /// <summary>
    /// Verifies <paramref name="tag"/> and decrypts <paramref name="ciphertext"/> into
    /// <paramref name="plaintext"/> using the concrete AEAD primitive.
    /// </summary>
    /// <param name="key">The encryption key.</param>
    /// <param name="nonce">The nonce used during encryption.</param>
    /// <param name="ciphertext">The ciphertext to decrypt, without the tag.</param>
    /// <param name="tag">The authentication tag to verify.</param>
    /// <param name="plaintext">The span that receives the recovered plaintext; the same length as <paramref name="ciphertext"/>.</param>
    /// <param name="associatedData">The additional authenticated data supplied during encryption.</param>
    protected abstract void DecryptCore(
        byte[] key,
        byte[] nonce,
        ReadOnlySpan<byte> ciphertext,
        ReadOnlySpan<byte> tag,
        Span<byte> plaintext,
        byte[] associatedData
    );
}
