using System.Security.Cryptography;
using System.Text;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;

using Org.BouncyCastle.Crypto.Generators;

namespace BackupZCrypt.Infrastructure.Strategies.KeyDerivation;

/// <summary>
/// Key-derivation strategy using the memory-hard scrypt function (BouncyCastle), tuned with
/// fixed cost, block-size, and parallelization parameters. The UTF-8 password bytes are
/// cleared in a <c>finally</c> block once derivation completes.
/// </summary>
internal sealed class ScryptKeyDerivationStrategy : IKeyDerivationAlgorithmStrategy
{
    private const int CostParameter = 262144;
    private const int BlockSize = 8;
    private const int Parallelization = 1;

    /// <summary>
    /// Gets the algorithm identifier (<see cref="KeyDerivationAlgorithm.Scrypt"/>) used to select this strategy.
    /// </summary>
    public KeyDerivationAlgorithm Id => KeyDerivationAlgorithm.Scrypt;

    /// <summary>
    /// Derives a key of the requested size from a password and salt using scrypt.
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
            return SCrypt.Generate(
                passwordBytes,
                salt,
                CostParameter,
                BlockSize,
                Parallelization,
                keySize / 8
            );
        }
        catch (Exception ex)
        {
            throw new CryptographicException("Failed to derive key with scrypt.", ex);
        }
        finally
        {
            if (passwordBytes is not null)
            {
                Array.Clear(passwordBytes, 0, passwordBytes.Length);
            }
        }
    }
}
