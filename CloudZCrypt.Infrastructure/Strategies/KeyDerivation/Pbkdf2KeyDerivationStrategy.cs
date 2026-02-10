using System.Security.Cryptography;
using System.Text;
using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Strategies.Interfaces;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;

namespace CloudZCrypt.Infrastructure.Strategies.KeyDerivation;

internal class Pbkdf2KeyDerivationStrategy : IKeyDerivationAlgorithmStrategy
{
    private const int Iterations = 800000;

    public KeyDerivationAlgorithm Id => KeyDerivationAlgorithm.PBKDF2;

    public string DisplayName => "PBKDF2 (HMAC-SHA256)";

    public string Description =>
        "A widely standardized (PKCS #5, RFC 8018, FIPS 140 allowed) iterative, CPU‑bound key derivation function using repeated HMAC-SHA256 applications. "
        + "Simple and broadly implemented in virtually all cryptographic libraries. "
        + "Lacks intrinsic memory hardness, making it comparatively cheaper to accelerate on GPUs/ASICs versus Argon2id or scrypt. "
        + "Still appropriate where regulatory, legacy platform, or FIPS compliance requirements dominate, or when only conservative primitives are permitted. "
        + "Security hinges on high iteration counts (cost parameter) and high‑entropy passwords.";

    public string Summary => "Best for maximum compatibility / legacy & compliance needs";

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
            throw new CryptographicException("Error deriving key with PBKDF2", ex);
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
