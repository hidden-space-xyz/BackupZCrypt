using System.Security.Cryptography;
using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Strategies.Interfaces;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;

namespace CloudZCrypt.Infrastructure.Strategies.KeyDerivation;

internal class Argon2IdKeyDerivationStrategy : IKeyDerivationAlgorithmStrategy
{
    private const int MemoryCost = 65536;
    private const int Iterations = 4;
    private const int Parallelism = 4;

    public KeyDerivationAlgorithm Id => KeyDerivationAlgorithm.Argon2id;

    public string DisplayName => "Argon2id";

    public string Description =>
        "A modern memory‑hard password hashing and key derivation function (PHC winner). "
        + "The “id” variant blends Argon2i (side‑channel resistant) and Argon2d (GPU/ASIC resistance) for balanced security. "
        + "Tunable via: memory cost (m), iterations/time cost (t), and parallelism (p). "
        + "Provides strong resistance to large‑scale brute force on GPUs, FPGAs, and ASICs by imposing substantial RAM requirements. "
        + "Supports domain separation with distinct salt plus optional secret/data parameters. "
        + "Recommended where modern security is prioritized over legacy compatibility.";

    public string Summary => "Best for maximum security / modern memory‑hard password hashing";

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

        byte[] key = new byte[keySize / 8];
        char[]? passwordChars = null;

        try
        {
            passwordChars = password.ToCharArray();
            argon2.GenerateBytes(passwordChars, key);
            return key;
        }
        catch (Exception ex)
        {
            if (key != null)
            {
                Array.Clear(key, 0, key.Length);
            }

            throw new CryptographicException("Error deriving key with Argon2id", ex);
        }
        finally
        {
            if (passwordChars != null)
            {
                Array.Clear(passwordChars, 0, passwordChars.Length);
            }
        }
    }
}
