using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;

namespace BackupZCrypt.Infrastructure.Strategies.Encryption;

// Pass-through strategy that lets unencrypted backups flow through the chunked
// pipeline (compression, deduplication, manifest). Stored data is neither
// confidential nor tamper-protected; integrity relies on the manifest hashes.
internal sealed class NoneEncryptionStrategy : IEncryptionAlgorithmStrategy
{
    public EncryptionAlgorithm Id => EncryptionAlgorithm.None;

    public byte[] EncryptChunk(
        ReadOnlySpan<byte> plaintext,
        byte[] key,
        byte[] nonce,
        byte[] associatedData
    )
    {
        return plaintext.ToArray();
    }

    public byte[] DecryptChunk(
        ReadOnlySpan<byte> ciphertext,
        byte[] key,
        byte[] nonce,
        byte[] associatedData
    )
    {
        return ciphertext.ToArray();
    }
}
