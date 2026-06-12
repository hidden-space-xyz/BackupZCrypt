using System.Security.Cryptography;
using System.Text;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Infrastructure.Resources;

namespace BackupZCrypt.Infrastructure.Strategies.KeyDerivation;

internal sealed class Pbkdf2KeyDerivationStrategy : IKeyDerivationAlgorithmStrategy
{
    private const int Iterations = 800000;

    public KeyDerivationAlgorithm Id => KeyDerivationAlgorithm.PBKDF2;

    public string DisplayName => Messages.Pbkdf2DisplayName;

    public string Description => Messages.Pbkdf2Description;

    public string Summary => Messages.Pbkdf2Summary;

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
            throw new CryptographicException(Messages.Pbkdf2KeyDerivationError, ex);
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
