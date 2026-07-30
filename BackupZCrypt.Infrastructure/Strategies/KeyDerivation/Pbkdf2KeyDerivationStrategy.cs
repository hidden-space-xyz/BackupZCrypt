using System.Security.Cryptography;
using System.Text;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;

namespace BackupZCrypt.Infrastructure.Strategies.KeyDerivation;

/// <summary>
/// Key-derivation strategy using PBKDF2 with HMAC-SHA256 and a high fixed iteration count.
/// The UTF-8 password bytes are zeroed in a <c>finally</c> block once derivation completes.
/// </summary>
internal sealed class Pbkdf2KeyDerivationStrategy : IKeyDerivationAlgorithmStrategy
{
    /// <summary>
    /// The number of PBKDF2 rounds (800000). PBKDF2 is not memory-hard, so the iteration count is
    /// the only lever available to make an offline password guess expensive.
    /// </summary>
    private const int Iterations = 800000;

    /// <summary>
    /// Gets the algorithm identifier (<see cref="KeyDerivationAlgorithm.PBKDF2"/>) used to select this strategy.
    /// </summary>
    public KeyDerivationAlgorithm Id => KeyDerivationAlgorithm.PBKDF2;

    /// <summary>
    /// Derives a key of the requested size from a password and salt using PBKDF2-HMAC-SHA256.
    /// </summary>
    /// <param name="password">The password to derive the key from.</param>
    /// <param name="salt">The salt that makes the derivation unique per backup.</param>
    /// <param name="keySize">The desired key length in bits.</param>
    /// <returns>The derived key, <paramref name="keySize"/> bits in length.</returns>
    /// <exception cref="CryptographicException">Key derivation fails.</exception>
    public byte[] DeriveKey(string password, byte[] salt, int keySize)
    {
        byte[]? passwordBytes = null;

        try
        {
            passwordBytes = Encoding.UTF8.GetBytes(password);
            return Rfc2898DeriveBytes.Pbkdf2(
                passwordBytes,
                salt,
                Iterations,
                HashAlgorithmName.SHA256,
                keySize / 8
            );
        }
        catch (Exception ex)
        {
            throw new CryptographicException("Failed to derive key with PBKDF2.", ex);
        }
        finally
        {
            if (passwordBytes is not null)
            {
                CryptographicOperations.ZeroMemory(passwordBytes);
            }
        }
    }
}
