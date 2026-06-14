namespace BackupZCrypt.Domain.Enums;

/// <summary>
/// Identifies the AEAD cipher used to encrypt chunks and the manifest.
/// </summary>
public enum EncryptionAlgorithm
{
    /// <summary>
    /// AES in an authenticated (GCM) mode.
    /// </summary>
    Aes = 0,

    /// <summary>
    /// Twofish block cipher.
    /// </summary>
    Twofish = 1,

    /// <summary>
    /// Serpent block cipher.
    /// </summary>
    Serpent = 2,

    /// <summary>
    /// ChaCha20 stream cipher with Poly1305 authentication.
    /// </summary>
    ChaCha20 = 3,

    /// <summary>
    /// Camellia block cipher.
    /// </summary>
    Camellia = 4,
}
