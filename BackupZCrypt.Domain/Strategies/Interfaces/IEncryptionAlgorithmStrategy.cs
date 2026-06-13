using BackupZCrypt.Domain.Enums;

namespace BackupZCrypt.Domain.Strategies.Interfaces;

public interface IEncryptionAlgorithmStrategy
{
    EncryptionAlgorithm Id { get; }

    byte[] EncryptChunk(
        ReadOnlySpan<byte> plaintext,
        byte[] key,
        byte[] nonce,
        byte[] associatedData
    );

    byte[] DecryptChunk(
        ReadOnlySpan<byte> ciphertext,
        byte[] key,
        byte[] nonce,
        byte[] associatedData
    );
}
