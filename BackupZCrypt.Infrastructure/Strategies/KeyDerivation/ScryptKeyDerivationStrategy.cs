using System.Security.Cryptography;
using System.Text;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;
using Org.BouncyCastle.Crypto.Generators;

namespace BackupZCrypt.Infrastructure.Strategies.KeyDerivation;

internal sealed class ScryptKeyDerivationStrategy : IKeyDerivationAlgorithmStrategy
{
    private const int CostParameter = 262144;
    private const int BlockSize = 8;
    private const int Parallelization = 1;

    public KeyDerivationAlgorithm Id => KeyDerivationAlgorithm.Scrypt;

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
