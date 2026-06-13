namespace BackupZCrypt.Domain.Enums;

/// <summary>
/// Identifies the AEAD cipher used to encrypt chunks and the manifest.
/// </summary>
public enum EncryptionAlgorithm
{
    /// <summary>
    /// No encryption algorithm selected.
    /// </summary>
    None = 0,

    /// <summary>
    /// AES in an authenticated (GCM) mode.
    /// </summary>
    Aes = 1,

    /// <summary>
    /// Twofish block cipher.
    /// </summary>
    Twofish = 2,

    /// <summary>
    /// Serpent block cipher.
    /// </summary>
    Serpent = 3,

    /// <summary>
    /// ChaCha20 stream cipher with Poly1305 authentication.
    /// </summary>
    ChaCha20 = 4,

    /// <summary>
    /// Camellia block cipher.
    /// </summary>
    Camellia = 5,
}
