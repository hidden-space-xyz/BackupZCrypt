using System.Security.Cryptography;

using BackupZCrypt.Application.Utilities.Helpers;
using BackupZCrypt.Domain.Constants;

namespace BackupZCrypt.Test.Unit.Application;

/// <summary>
/// Unit tests for the per-chunk nonce and associated-data derivation that binds a chunk's ciphertext
/// to its content. Both values are part of the on-disk format rather than an implementation detail:
/// the nonce must stay deterministic for deduplication to work, key-dependent so identical content
/// under two passwords never reuses a nonce, and exactly
/// <see cref="EncryptionConstants.NonceSize"/> bytes long, while the associated-data layout is what
/// every previously written archive was authenticated under.
/// </summary>
public sealed class ChunkCryptoHelperTests
{
    [Test]
    public void ComputeChunkNonce_SameKeyAndChunkHash_ReturnsIdenticalNonceOfExactlyNonceSize()
    {
        var nonceKey = SHA256.HashData("chunk-nonce-sub-key"u8);
        var chunkHash = SHA256.HashData("chunk plaintext"u8);

        var first = ChunkCryptoHelper.ComputeChunkNonce(nonceKey, chunkHash);
        var second = ChunkCryptoHelper.ComputeChunkNonce(nonceKey, chunkHash);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                first,
                Has.Length.EqualTo(EncryptionConstants.NonceSize),
                "The nonce length is part of the format; a silently truncated or widened nonce breaks every archive."
            );
            Assert.That(
                second,
                Is.EqualTo(first),
                "The nonce must be deterministic, otherwise identical chunks stop deduplicating."
            );
        }
    }

    [Test]
    public void ComputeChunkNonce_NonceKeysDifferingByOneBit_ReturnDifferentNonces()
    {
        var chunkHash = SHA256.HashData("chunk plaintext"u8);
        var nonceKey = SHA256.HashData("chunk-nonce-sub-key"u8);
        var neighbouringKey = SHA256.HashData("chunk-nonce-sub-key"u8);
        neighbouringKey[0] ^= 0x01;

        var nonce = ChunkCryptoHelper.ComputeChunkNonce(nonceKey, chunkHash);
        var neighbouringNonce = ChunkCryptoHelper.ComputeChunkNonce(neighbouringKey, chunkHash);

        Assert.That(
            neighbouringNonce,
            Is.Not.EqualTo(nonce),
            "The nonce must depend on the key, otherwise two backups of the same content share a nonce."
        );
    }

    [Test]
    public void ComputeChunkNonce_ChunkHashesDifferingByOneBit_ReturnDifferentNonces()
    {
        var nonceKey = SHA256.HashData("chunk-nonce-sub-key"u8);
        var chunkHash = SHA256.HashData("chunk plaintext"u8);
        var neighbouringHash = SHA256.HashData("chunk plaintext"u8);
        neighbouringHash[0] ^= 0x01;

        var nonce = ChunkCryptoHelper.ComputeChunkNonce(nonceKey, chunkHash);
        var neighbouringNonce = ChunkCryptoHelper.ComputeChunkNonce(nonceKey, neighbouringHash);

        Assert.That(
            neighbouringNonce,
            Is.Not.EqualTo(nonce),
            "Distinct chunks must not reuse a nonce under the same key."
        );
    }

    [Test]
    public void BuildChunkAssociatedData_ChunkHashAndNonce_ConcatenatesHashBeforeNonce()
    {
        var chunkHash = SHA256.HashData("chunk plaintext"u8);
        var nonce = ChunkCryptoHelper.ComputeChunkNonce(
            SHA256.HashData("chunk-nonce-sub-key"u8),
            chunkHash
        );

        var associatedData = ChunkCryptoHelper.BuildChunkAssociatedData(chunkHash, nonce);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(associatedData, Has.Length.EqualTo(chunkHash.Length + nonce.Length));
            Assert.That(associatedData[..chunkHash.Length], Is.EqualTo(chunkHash));
            Assert.That(associatedData[chunkHash.Length..], Is.EqualTo(nonce));
        }
    }
}
