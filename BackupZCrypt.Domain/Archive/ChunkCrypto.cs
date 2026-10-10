using System.Security.Cryptography;

using BackupZCrypt.Domain.Constants;

namespace BackupZCrypt.Domain.Archive;

/// <summary>
/// Derives the per-chunk cryptographic values that bind a chunk's ciphertext to its content: the
/// deterministic AEAD nonce, the associated data, and the keyed file name the chunk is stored under.
/// Centralized here so every service that encrypts or decrypts chunks (backup, restore, verify, and
/// the benchmark) shares one identical layout and cannot silently diverge.
/// </summary>
public static class ChunkCrypto
{
    /// <summary>
    /// Derives the deterministic AEAD nonce for a chunk as the first
    /// <see cref="EncryptionConstants.NonceSize"/> bytes of <c>HMAC-SHA256(nonceKey, chunkHash)</c>.
    /// </summary>
    /// <param name="nonceKey">The purpose-bound sub-key used to derive chunk nonces.</param>
    /// <param name="chunkHash">The content hash of the chunk.</param>
    /// <returns>The per-chunk nonce.</returns>
    public static byte[] ComputeChunkNonce(byte[] nonceKey, byte[] chunkHash)
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
    public static byte[] BuildChunkAssociatedData(byte[] chunkHash, byte[] nonce)
    {
        var associatedData = new byte[chunkHash.Length + nonce.Length];
        chunkHash.CopyTo(associatedData, 0);
        nonce.CopyTo(associatedData, chunkHash.Length);
        return associatedData;
    }

    /// <summary>
    /// Computes a chunk's on-disk file name: the lowercase hex of <c>HMAC-SHA256(namingKey, chunkHash)</c>
    /// followed by <see cref="BackupConstants.AppFileExtension"/>, the shape
    /// <see cref="BackupLayout.IsChunkFileName"/> recognizes.
    /// </summary>
    /// <remarks>
    /// Keying the name keeps the content hashes off disk, so someone reading the chunks directory
    /// cannot test whether a known file is part of the backup. The intermediate HMAC is zeroed.
    /// </remarks>
    /// <param name="namingKey">The chunk-naming sub-key.</param>
    /// <param name="chunkHash">The SHA-256 content hash of the chunk.</param>
    /// <returns>The chunk file name with its extension.</returns>
    public static string ComputeChunkFileName(byte[] namingKey, byte[] chunkHash)
    {
        var hmac = HMACSHA256.HashData(namingKey, chunkHash);
        try
        {
            return Convert.ToHexStringLower(hmac) + BackupConstants.AppFileExtension;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(hmac);
        }
    }
}
