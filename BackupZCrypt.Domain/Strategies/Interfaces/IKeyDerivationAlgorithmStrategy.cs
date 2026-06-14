using BackupZCrypt.Domain.Enums;

namespace BackupZCrypt.Domain.Strategies.Interfaces;

/// <summary>
/// Derives a symmetric key from a password and salt using a specific key derivation function.
/// </summary>
public interface IKeyDerivationAlgorithmStrategy
{
    /// <summary>
    /// Gets the algorithm this strategy implements, used to select it by enum value.
    /// </summary>
    public KeyDerivationAlgorithm Id { get; }

    /// <summary>
    /// Derives a key of the requested size from the given password and salt.
    /// </summary>
    /// <param name="password">The user-supplied password.</param>
    /// <param name="salt">The salt that makes the derivation unique.</param>
    /// <param name="keySize">The desired key size in bytes.</param>
    /// <returns>The derived key.</returns>
    public byte[] DeriveKey(string password, byte[] salt, int keySize);
}
