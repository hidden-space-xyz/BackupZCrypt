using System.Security.Cryptography;

using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;

using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

namespace BackupZCrypt.Infrastructure.Strategies.Encryption;

/// <summary>
/// Base class for AEAD encryption strategies backed by BouncyCastle GCM block ciphers
/// (used for block algorithms the platform does not provide natively, for example Twofish,
/// Serpent, Camellia). Handles the shared GCM encrypt/decrypt flow with the authentication
/// tag appended to the ciphertext and zeroes intermediate plaintext/output buffers;
/// derived types supply the concrete cipher via <see cref="CreateCipher"/>.
/// </summary>
internal abstract class BouncyCastleGcmEncryptionStrategyBase : IEncryptionAlgorithmStrategy
{
    /// <summary>
    /// The GCM authentication tag length in bits (128). BouncyCastle's <see cref="AeadParameters"/>
    /// expects the MAC size in bits, unlike the platform primitives that take it in bytes.
    /// </summary>
    private const int MacSize = EncryptionConstants.MacSize;

    /// <summary>
    /// Gets the algorithm identifier used to select the concrete strategy.
    /// </summary>
    public abstract EncryptionAlgorithm Id { get; }

    /// <summary>
    /// Encrypts a single chunk with the derived type's GCM cipher and returns the ciphertext
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
        var input = plaintext.ToArray();

        try
        {
            var cipher = CreateCipher();
            AeadParameters parameters = new(new KeyParameter(key), MacSize, nonce, associatedData);
            cipher.Init(true, parameters);

            var output = new byte[cipher.GetOutputSize(input.Length)];
            var len = cipher.ProcessBytes(input, 0, input.Length, output, 0);
            len += cipher.DoFinal(output, len);

            if (len < output.Length)
            {
                var result = output[..len];
                CryptographicOperations.ZeroMemory(output);
                return result;
            }

            return output;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
        }
    }

    /// <summary>
    /// Verifies the appended authentication tag and decrypts a single chunk with the derived
    /// type's GCM cipher. On any failure the output buffer is zeroed before the exception propagates.
    /// </summary>
    /// <param name="ciphertext">The ciphertext with the authentication tag appended.</param>
    /// <param name="key">The encryption key.</param>
    /// <param name="nonce">The nonce used during encryption.</param>
    /// <param name="associatedData">The additional authenticated data supplied during encryption.</param>
    /// <returns>The recovered plaintext.</returns>
    /// <exception cref="CryptographicException">
    /// Authentication or decryption fails, such as on tampering or a wrong key
    /// (a BouncyCastle <c>InvalidCipherTextException</c> is wrapped as a <see cref="CryptographicException"/>).
    /// </exception>
    public byte[] DecryptChunk(
        ReadOnlySpan<byte> ciphertext,
        byte[] key,
        byte[] nonce,
        byte[] associatedData
    )
    {
        var input = ciphertext.ToArray();
        var cipher = CreateCipher();
        AeadParameters parameters = new(new KeyParameter(key), MacSize, nonce, associatedData);
        cipher.Init(false, parameters);

        var output = new byte[cipher.GetOutputSize(input.Length)];

        try
        {
            var len = cipher.ProcessBytes(input, 0, input.Length, output, 0);
            len += cipher.DoFinal(output, len);

            if (len < output.Length)
            {
                var result = output[..len];
                CryptographicOperations.ZeroMemory(output);
                return result;
            }

            return output;
        }
        catch (InvalidCipherTextException ex)
        {
            CryptographicOperations.ZeroMemory(output);
            throw new CryptographicException(ex.Message, ex);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(output);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
        }
    }

    /// <summary>
    /// Creates a fresh, uninitialized AEAD GCM cipher instance for the concrete algorithm.
    /// A new instance is returned per call so encryption and decryption never share cipher state.
    /// </summary>
    /// <returns>The GCM-mode AEAD cipher to use for the current operation.</returns>
    protected abstract IAeadCipher CreateCipher();
}
