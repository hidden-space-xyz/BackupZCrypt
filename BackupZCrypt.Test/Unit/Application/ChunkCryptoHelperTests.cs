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
    [Fact]
    internal void ComputeChunkNonce_SameKeyAndChunkHash_ReturnsIdenticalNonceOfExactlyNonceSize()
    {
        var nonceKey = SHA256.HashData("chunk-nonce-sub-key"u8);
        var chunkHash = SHA256.HashData("chunk plaintext"u8);

        var first = ChunkCryptoHelper.ComputeChunkNonce(nonceKey, chunkHash);
        var second = ChunkCryptoHelper.ComputeChunkNonce(nonceKey, chunkHash);

        Assert.Multiple(
            () => Assert.Equal(EncryptionConstants.NonceSize, first.Length),
            () => Assert.Equal(first, second)
        );
    }

    [Fact]
    internal void ComputeChunkNonce_NonceKeysDifferingByOneBit_ReturnDifferentNonces()
    {
        var chunkHash = SHA256.HashData("chunk plaintext"u8);
        var nonceKey = SHA256.HashData("chunk-nonce-sub-key"u8);
        var neighbouringKey = SHA256.HashData("chunk-nonce-sub-key"u8);
        neighbouringKey[0] ^= 0x01;

        var nonce = ChunkCryptoHelper.ComputeChunkNonce(nonceKey, chunkHash);
        var neighbouringNonce = ChunkCryptoHelper.ComputeChunkNonce(neighbouringKey, chunkHash);

        Assert.NotEqual(nonce, neighbouringNonce);
    }

    [Fact]
    internal void ComputeChunkNonce_ChunkHashesDifferingByOneBit_ReturnDifferentNonces()
    {
        var nonceKey = SHA256.HashData("chunk-nonce-sub-key"u8);
        var chunkHash = SHA256.HashData("chunk plaintext"u8);
        var neighbouringHash = SHA256.HashData("chunk plaintext"u8);
        neighbouringHash[0] ^= 0x01;

        var nonce = ChunkCryptoHelper.ComputeChunkNonce(nonceKey, chunkHash);
        var neighbouringNonce = ChunkCryptoHelper.ComputeChunkNonce(nonceKey, neighbouringHash);

        Assert.NotEqual(nonce, neighbouringNonce);
    }

    [Fact]
    internal void BuildChunkAssociatedData_ChunkHashAndNonce_ConcatenatesHashBeforeNonce()
    {
        var chunkHash = SHA256.HashData("chunk plaintext"u8);
        var nonce = ChunkCryptoHelper.ComputeChunkNonce(
            SHA256.HashData("chunk-nonce-sub-key"u8),
            chunkHash
        );

        var associatedData = ChunkCryptoHelper.BuildChunkAssociatedData(chunkHash, nonce);

        Assert.Multiple(
            () => Assert.Equal(chunkHash.Length + nonce.Length, associatedData.Length),
            () => Assert.Equal(chunkHash, associatedData[..chunkHash.Length]),
            () => Assert.Equal(nonce, associatedData[chunkHash.Length..])
        );
    }
}
