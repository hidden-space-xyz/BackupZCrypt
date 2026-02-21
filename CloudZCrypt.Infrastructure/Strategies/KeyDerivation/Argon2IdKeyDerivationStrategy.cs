namespace CloudZCrypt.Infrastructure.Strategies.KeyDerivation;

using System.Security.Cryptography;
using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Strategies.Interfaces;
using CloudZCrypt.Infrastructure.Resources;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;

internal class Argon2IdKeyDerivationStrategy : IKeyDerivationAlgorithmStrategy
{
    private const int MemoryCost = 262144;
    private const int Iterations = 4;
    private const int Parallelism = 2;

    public KeyDerivationAlgorithm Id => KeyDerivationAlgorithm.Argon2id;

    public string DisplayName => Messages.Argon2idDisplayName;

    public string Description => Messages.Argon2idDescription;

    public string Summary => Messages.Argon2idSummary;

    public byte[] DeriveKey(string password, byte[] salt, int keySize)
    {
        Argon2BytesGenerator argon2 = new();
        argon2.Init(
            new Argon2Parameters.Builder(Argon2Parameters.Argon2id)
                .WithSalt(salt)
                .WithMemoryAsKB(MemoryCost)
                .WithIterations(Iterations)
                .WithParallelism(Parallelism)
                .Build());

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
            Array.Clear(key, 0, key.Length);

            throw new CryptographicException(Messages.Argon2idKeyDerivationError, ex);
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
