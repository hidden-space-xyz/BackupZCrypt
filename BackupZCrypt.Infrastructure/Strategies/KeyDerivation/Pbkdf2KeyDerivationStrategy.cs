namespace BackupZCrypt.Infrastructure.Strategies.KeyDerivation;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Infrastructure.Resources;
using System.Security.Cryptography;
using System.Text;

internal class Pbkdf2KeyDerivationStrategy : IKeyDerivationAlgorithmStrategy
{
    private const int Iterations = 800000;

    public KeyDerivationAlgorithm Id => KeyDerivationAlgorithm.PBKDF2;

    public string DisplayName => Messages.Pbkdf2DisplayName;

    public string Description => Messages.Pbkdf2Description;

    public string Summary => Messages.Pbkdf2Summary;

    public byte[] DeriveKey(string password, byte[] salt, int keySize)
    {
        byte[]? passwordBytes = null;
        byte[]? key = null;

        try
        {
            passwordBytes = Encoding.UTF8.GetBytes(password);
            key = Rfc2898DeriveBytes.Pbkdf2(
                passwordBytes,
                salt,
                Iterations,
                HashAlgorithmName.SHA1,
                keySize / 8);

            var result = new byte[key.Length];
            Array.Copy(key, result, key.Length);
            return result;
        }
        catch (Exception ex)
        {
            throw new CryptographicException(Messages.Pbkdf2KeyDerivationError, ex);
        }
        finally
        {
            if (passwordBytes is not null)
            {
                CryptographicOperations.ZeroMemory(passwordBytes);
            }

            if (key is not null)
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }
    }
}
