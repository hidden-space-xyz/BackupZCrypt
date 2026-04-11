namespace BackupZCrypt.Infrastructure.Strategies.KeyDerivation;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Infrastructure.Resources;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using System.Security.Cryptography;

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

        var key = new byte[keySize / 8];
        char[] passwordChars = [];

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
            Array.Clear(passwordChars, 0, passwordChars.Length);
        }
    }
}
