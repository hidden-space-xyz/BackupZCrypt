namespace BackupZCrypt.Domain.Constants;

/// <summary>
/// Fixed cryptographic sizes shared by the encryption and key derivation strategies.
/// </summary>
public static class EncryptionConstants
{
    /// <summary>
    /// The symmetric key size in bits, applied to the password-derived master key and to every HKDF-derived sub-key.
    /// </summary>
    public const int KeySize = 256;

    /// <summary>
    /// The salt size in bytes used for key derivation.
    /// </summary>
    public const int SaltSize = 32;

    /// <summary>
    /// The AEAD nonce size in bytes.
    /// </summary>
    public const int NonceSize = 12;

    /// <summary>
    /// The authentication tag (MAC) size in bits.
    /// </summary>
    public const int MacSize = 128;

    /// <summary>
    /// The authentication tag size in bytes, derived from <see cref="MacSize"/>.
    /// </summary>
    public const int TagSize = MacSize / 8;
}
