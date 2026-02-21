namespace BackupZCrypt.Infrastructure.Strategies.KeyDerivation;

using System.Security.Cryptography;
using System.Text;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Infrastructure.Resources;
using Org.BouncyCastle.Crypto.Generators;

internal class ScryptKeyDerivationStrategy : IKeyDerivationAlgorithmStrategy
{
    private const int CostParameter = 262144;
    private const int BlockSize = 8;
    private const int Parallelization = 1;

    public KeyDerivationAlgorithm Id => KeyDerivationAlgorithm.Scrypt;

    public string DisplayName => Messages.ScryptDisplayName;

    public string Description => Messages.ScryptDescription;

    public string Summary => Messages.ScryptSummary;

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
                keySize / 8);
        }
        catch (Exception ex)
        {
            throw new CryptographicException(Messages.ScryptKeyDerivationError, ex);
        }
        finally
        {
            if (passwordBytes != null)
            {
                Array.Clear(passwordBytes, 0, passwordBytes.Length);
            }
        }
    }
}
