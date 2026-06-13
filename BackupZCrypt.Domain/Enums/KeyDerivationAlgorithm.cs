namespace BackupZCrypt.Domain.Enums;

/// <summary>
/// Identifies the password-based key derivation function used to derive the master key.
/// </summary>
public enum KeyDerivationAlgorithm
{
    /// <summary>
    /// Argon2id, a memory-hard function resistant to GPU and side-channel attacks.
    /// </summary>
    Argon2id = 0,

    /// <summary>
    /// PBKDF2, an iterated HMAC-based key derivation function.
    /// </summary>
    PBKDF2 = 1,

    /// <summary>
    /// Scrypt, a memory-hard sequential function.
    /// </summary>
    Scrypt = 2,
}
