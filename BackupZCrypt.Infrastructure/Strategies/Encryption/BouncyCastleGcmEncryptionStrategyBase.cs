using System.Security.Cryptography;
using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

namespace BackupZCrypt.Infrastructure.Strategies.Encryption;

internal abstract class BouncyCastleGcmEncryptionStrategyBase : IEncryptionAlgorithmStrategy
{
    private const int MacSize = EncryptionConstants.MacSize;

    public abstract EncryptionAlgorithm Id { get; }

    public byte[] EncryptChunk(
        ReadOnlySpan<byte> plaintext,
        byte[] key,
        byte[] nonce,
        byte[] associatedData
    )
    {
        var input = plaintext.ToArray();

        try
        {
            var cipher = CreateCipher();
            AeadParameters parameters = new(new KeyParameter(key), MacSize, nonce, associatedData);
            cipher.Init(true, parameters);

            var output = new byte[cipher.GetOutputSize(input.Length)];
            var len = cipher.ProcessBytes(input, 0, input.Length, output, 0);
            len += cipher.DoFinal(output, len);

            if (len < output.Length)
            {
                var result = output[..len];
                CryptographicOperations.ZeroMemory(output);
                return result;
            }

            return output;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
        }
    }

    public byte[] DecryptChunk(
        ReadOnlySpan<byte> ciphertext,
        byte[] key,
        byte[] nonce,
        byte[] associatedData
    )
    {
        var input = ciphertext.ToArray();
        var cipher = CreateCipher();
        AeadParameters parameters = new(new KeyParameter(key), MacSize, nonce, associatedData);
        cipher.Init(false, parameters);

        var output = new byte[cipher.GetOutputSize(input.Length)];

        try
        {
            var len = cipher.ProcessBytes(input, 0, input.Length, output, 0);
            len += cipher.DoFinal(output, len);

            if (len < output.Length)
            {
                var result = output[..len];
                CryptographicOperations.ZeroMemory(output);
                return result;
            }

            return output;
        }
        catch (InvalidCipherTextException ex)
        {
            CryptographicOperations.ZeroMemory(output);
            throw new CryptographicException(ex.Message, ex);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(output);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
        }
    }

    protected abstract IAeadCipher CreateCipher();
}
