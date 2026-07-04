using System.Security.Cryptography;

using BackupZCrypt.Domain.Enums;

namespace BackupZCrypt.Infrastructure.Strategies.Encryption;

/// <summary>
/// AEAD encryption strategy using ChaCha20-Poly1305 via the platform
/// <see cref="ChaCha20Poly1305"/> primitive. The Poly1305 authentication tag is appended to the ciphertext.
/// </summary>
internal sealed class ChaCha20EncryptionStrategy : PlatformAeadEncryptionStrategyBase
{
    /// <summary>
    /// Gets the algorithm identifier (<see cref="EncryptionAlgorithm.ChaCha20"/>) used to select this strategy.
    /// </summary>
    public override EncryptionAlgorithm Id => EncryptionAlgorithm.ChaCha20;

    /// <inheritdoc/>
    protected override void EncryptCore(
        byte[] key,
        byte[] nonce,
        ReadOnlySpan<byte> plaintext,
        Span<byte> ciphertext,
        Span<byte> tag,
        byte[] associatedData
    )
    {
        using ChaCha20Poly1305 cipher = new(key);
        cipher.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
    }

    /// <inheritdoc/>
    protected override void DecryptCore(
        byte[] key,
        byte[] nonce,
        ReadOnlySpan<byte> ciphertext,
        ReadOnlySpan<byte> tag,
        Span<byte> plaintext,
        byte[] associatedData
    )
    {
        using ChaCha20Poly1305 cipher = new(key);
        cipher.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
    }
}
