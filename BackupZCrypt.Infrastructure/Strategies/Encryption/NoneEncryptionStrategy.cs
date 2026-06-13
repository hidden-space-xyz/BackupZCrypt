using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;

namespace BackupZCrypt.Infrastructure.Strategies.Encryption;

/// <summary>
/// Pass-through encryption strategy that lets unencrypted backups flow through the chunked
/// pipeline (compression, deduplication, manifest). Stored data is neither confidential nor
/// tamper-protected; integrity relies solely on the manifest hashes. The key, nonce, and
/// associated-data arguments are accepted for interface compatibility and ignored.
/// </summary>
internal sealed class NoneEncryptionStrategy : IEncryptionAlgorithmStrategy
{
    /// <summary>
    /// Gets the algorithm identifier (<see cref="EncryptionAlgorithm.None"/>) used to select this strategy.
    /// </summary>
    public EncryptionAlgorithm Id => EncryptionAlgorithm.None;

    /// <summary>
    /// Returns a copy of the supplied plaintext unchanged (no encryption is applied).
    /// </summary>
    /// <param name="plaintext">The chunk to pass through.</param>
    /// <param name="key">Ignored; present for interface compatibility.</param>
    /// <param name="nonce">Ignored; present for interface compatibility.</param>
    /// <param name="associatedData">Ignored; present for interface compatibility.</param>
    /// <returns>A copy of <paramref name="plaintext"/>.</returns>
    public byte[] EncryptChunk(
        ReadOnlySpan<byte> plaintext,
        byte[] key,
        byte[] nonce,
        byte[] associatedData
    )
    {
        return plaintext.ToArray();
    }

    /// <summary>
    /// Returns a copy of the supplied data unchanged (no decryption is applied).
    /// </summary>
    /// <param name="ciphertext">The stored chunk to pass through.</param>
    /// <param name="key">Ignored; present for interface compatibility.</param>
    /// <param name="nonce">Ignored; present for interface compatibility.</param>
    /// <param name="associatedData">Ignored; present for interface compatibility.</param>
    /// <returns>A copy of <paramref name="ciphertext"/>.</returns>
    public byte[] DecryptChunk(
        ReadOnlySpan<byte> ciphertext,
        byte[] key,
        byte[] nonce,
        byte[] associatedData
    )
    {
        return ciphertext.ToArray();
    }
}
