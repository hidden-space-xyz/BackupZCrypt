using System.Security.Cryptography;

using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Enums;

namespace BackupZCrypt.Infrastructure.Strategies.Encryption;

/// <summary>
/// AEAD encryption strategy using AES in Galois/Counter Mode (AES-GCM) via the platform
/// <see cref="AesGcm"/> primitive. The authentication tag is appended to the ciphertext.
/// </summary>
internal sealed class AesEncryptionStrategy : PlatformAeadEncryptionStrategyBase
{
    /// <summary>
    /// Gets the algorithm identifier (<see cref="EncryptionAlgorithm.Aes"/>) used to select this strategy.
    /// </summary>
    public override EncryptionAlgorithm Id => EncryptionAlgorithm.Aes;

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
        using AesGcm aes = new(key, EncryptionConstants.TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
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
        using AesGcm aes = new(key, EncryptionConstants.TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
    }
}
