namespace BackupZCrypt.Infrastructure.Strategies.ChunkCrypto;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Infrastructure.Constants;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

internal abstract class BouncyCastleChunkCryptoProviderBase : IChunkCryptoProvider
{
    private const int MacSize = EncryptionConstants.MacSize;

    public abstract EncryptionAlgorithm Id { get; }

    public byte[] EncryptChunk(
        ReadOnlySpan<byte> plaintext,
        byte[] key,
        byte[] nonce,
        byte[] associatedData)
    {
        var cipher = CreateCipher();
        AeadParameters parameters = new(new KeyParameter(key), MacSize, nonce, associatedData);
        cipher.Init(true, parameters);

        var output = new byte[cipher.GetOutputSize(plaintext.Length)];
        var len = cipher.ProcessBytes(plaintext.ToArray(), 0, plaintext.Length, output, 0);
        len += cipher.DoFinal(output, len);

        if (len < output.Length)
        {
            return output[..len];
        }

        return output;
    }

    public byte[] DecryptChunk(
        ReadOnlySpan<byte> ciphertext,
        byte[] key,
        byte[] nonce,
        byte[] associatedData)
    {
        var cipher = CreateCipher();
        AeadParameters parameters = new(new KeyParameter(key), MacSize, nonce, associatedData);
        cipher.Init(false, parameters);

        var output = new byte[cipher.GetOutputSize(ciphertext.Length)];
        var len = cipher.ProcessBytes(ciphertext.ToArray(), 0, ciphertext.Length, output, 0);
        len += cipher.DoFinal(output, len);

        if (len < output.Length)
        {
            return output[..len];
        }

        return output;
    }

    protected abstract IAeadCipher CreateCipher();
}
