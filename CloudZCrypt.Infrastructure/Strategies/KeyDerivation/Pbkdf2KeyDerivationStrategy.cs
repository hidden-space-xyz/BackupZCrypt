namespace CloudZCrypt.Infrastructure.Strategies.KeyDerivation;

using System.Security.Cryptography;
using System.Text;
using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Strategies.Interfaces;
using CloudZCrypt.Infrastructure.Resources;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;

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

            Pkcs5S2ParametersGenerator pbkdf2 = new();
            pbkdf2.Init(passwordBytes, salt, Iterations);

            KeyParameter keyParam = (KeyParameter)pbkdf2.GenerateDerivedMacParameters(keySize);
            key = keyParam.GetKey();

            byte[] result = new byte[key.Length];
            Array.Copy(key, result, key.Length);
            return result;
        }
        catch (Exception ex)
        {
            throw new CryptographicException(Messages.Pbkdf2KeyDerivationError, ex);
        }
        finally
        {
            if (passwordBytes != null)
            {
                Array.Clear(passwordBytes, 0, passwordBytes.Length);
            }

            if (key != null)
            {
                Array.Clear(key, 0, key.Length);
            }
        }
    }
}
