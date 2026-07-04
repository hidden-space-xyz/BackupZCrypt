using System.Security.Cryptography;

using BackupZCrypt.Domain.Constants;

namespace BackupZCrypt.Application.Utilities.Helpers;

/// <summary>
/// Derives the per-chunk cryptographic values that bind a chunk's ciphertext to its content: the
/// deterministic AEAD nonce and the associated data. Centralized here so every service that encrypts
/// or decrypts chunks (backup, restore, verify, and the benchmark) shares one identical layout and
/// cannot silently diverge.
/// </summary>
internal static class ChunkCryptoHelper
{
    /// <summary>
    /// Derives the deterministic AEAD nonce for a chunk as the first
    /// <see cref="EncryptionConstants.NonceSize"/> bytes of <c>HMAC-SHA256(nonceKey, chunkHash)</c>.
    /// </summary>
    /// <param name="nonceKey">The purpose-bound sub-key used to derive chunk nonces.</param>
    /// <param name="chunkHash">The content hash of the chunk.</param>
    /// <returns>The per-chunk nonce.</returns>
    internal static byte[] ComputeChunkNonce(byte[] nonceKey, byte[] chunkHash)
    {
        var hmac = HMACSHA256.HashData(nonceKey, chunkHash);
        var nonce = new byte[EncryptionConstants.NonceSize];

        try
        {
            Buffer.BlockCopy(hmac, 0, nonce, 0, EncryptionConstants.NonceSize);
            return nonce;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(hmac);
        }
    }

    /// <summary>
    /// Builds the AEAD associated data for a chunk as <c>chunkHash ‖ nonce</c>.
    /// </summary>
    /// <param name="chunkHash">The content hash of the chunk.</param>
    /// <param name="nonce">The per-chunk nonce.</param>
    /// <returns>The associated data bound to the chunk's ciphertext.</returns>
    internal static byte[] BuildChunkAssociatedData(byte[] chunkHash, byte[] nonce)
    {
        var associatedData = new byte[chunkHash.Length + nonce.Length];
        chunkHash.CopyTo(associatedData, 0);
        nonce.CopyTo(associatedData, chunkHash.Length);
        return associatedData;
    }
}
