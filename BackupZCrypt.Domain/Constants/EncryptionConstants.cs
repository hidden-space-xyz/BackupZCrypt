namespace BackupZCrypt.Domain.Constants;

/// <summary>
/// Fixed cryptographic sizes shared by the encryption and key derivation strategies.
/// </summary>
public static class EncryptionConstants
{
    /// <summary>
    /// Symmetric key size in bits.
    /// </summary>
    public const int KeySize = 256;

    /// <summary>
    /// Salt size in bytes used for key derivation.
    /// </summary>
    public const int SaltSize = 32;

    /// <summary>
    /// AEAD nonce size in bytes.
    /// </summary>
    public const int NonceSize = 12;

    /// <summary>
    /// Authentication tag (MAC) size in bits.
    /// </summary>
    public const int MacSize = 128;

    /// <summary>
    /// Authentication tag size in bytes, derived from <see cref="MacSize"/>.
    /// </summary>
    public const int TagSize = MacSize / 8;
}
