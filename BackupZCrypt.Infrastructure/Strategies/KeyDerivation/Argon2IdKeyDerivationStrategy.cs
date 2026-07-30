using System.Security.Cryptography;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;

using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;

namespace BackupZCrypt.Infrastructure.Strategies.KeyDerivation;

/// <summary>
/// Key-derivation strategy using the memory-hard Argon2id function (BouncyCastle), tuned with
/// fixed memory, iteration, and parallelism cost parameters. The password character buffer is
/// cleared in a <c>finally</c> block, and the derived key is cleared if derivation fails.
/// </summary>
internal sealed class Argon2IdKeyDerivationStrategy : IKeyDerivationAlgorithmStrategy
{
    /// <summary>
    /// The Argon2id memory cost in kibibytes (262144 KiB, or 256 MiB). A large working set is what
    /// denies an attacker cheap massively parallel guessing on GPUs or custom hardware.
    /// </summary>
    private const int MemoryCost = 262144;

    /// <summary>
    /// The Argon2id time cost: the number of passes made over the memory block.
    /// </summary>
    private const int Iterations = 4;

    /// <summary>
    /// The number of Argon2id lanes computed in parallel.
    /// </summary>
    private const int Parallelism = 2;

    /// <summary>
    /// Gets the algorithm identifier (<see cref="KeyDerivationAlgorithm.Argon2id"/>) used to select this strategy.
    /// </summary>
    public KeyDerivationAlgorithm Id => KeyDerivationAlgorithm.Argon2id;

    /// <summary>
    /// Derives a key of the requested size from a password and salt using Argon2id.
    /// </summary>
    /// <param name="password">The password to derive the key from.</param>
    /// <param name="salt">The salt that makes the derivation unique per backup.</param>
    /// <param name="keySize">The desired key length in bits.</param>
    /// <returns>The derived key, <paramref name="keySize"/> bits in length.</returns>
    /// <exception cref="CryptographicException">Key derivation fails.</exception>
    public byte[] DeriveKey(string password, byte[] salt, int keySize)
    {
        Argon2BytesGenerator argon2 = new();
        argon2.Init(
            new Argon2Parameters.Builder(Argon2Parameters.Argon2id)
                .WithSalt(salt)
                .WithMemoryAsKB(MemoryCost)
                .WithIterations(Iterations)
                .WithParallelism(Parallelism)
                .Build()
        );

        var key = new byte[keySize / 8];
        char[] passwordChars = [];

        try
        {
            passwordChars = password.ToCharArray();
            _ = argon2.GenerateBytes(passwordChars, key);
            return key;
        }
        catch (Exception ex)
        {
            Array.Clear(key, 0, key.Length);

            throw new CryptographicException("Failed to derive key with Argon2id.", ex);
        }
        finally
        {
            Array.Clear(passwordChars, 0, passwordChars.Length);
        }
    }
}
