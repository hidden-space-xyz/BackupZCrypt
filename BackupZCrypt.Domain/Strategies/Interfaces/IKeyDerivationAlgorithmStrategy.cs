using BackupZCrypt.Domain.Enums;

namespace BackupZCrypt.Domain.Strategies.Interfaces;

/// <summary>
/// Derives a symmetric key from a password and salt using a specific key derivation function.
/// </summary>
public interface IKeyDerivationAlgorithmStrategy : IAlgorithmStrategy<KeyDerivationAlgorithm>
{
    /// <summary>
    /// Derives a key of the requested size from the given password and salt.
    /// </summary>
    /// <param name="password">The user-supplied password.</param>
    /// <param name="salt">The salt that makes the derivation unique.</param>
    /// <param name="keySize">The desired key size in bits.</param>
    /// <returns>The derived key, <paramref name="keySize"/> bits in length.</returns>
    /// <exception cref="System.Security.Cryptography.CryptographicException">Key derivation fails.</exception>
    public byte[] DeriveKey(string password, byte[] salt, int keySize);
}
