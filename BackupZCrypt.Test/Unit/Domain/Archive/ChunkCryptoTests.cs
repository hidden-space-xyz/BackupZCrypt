using System.Security.Cryptography;

using BackupZCrypt.Domain.Archive;

namespace BackupZCrypt.Test.Unit.Domain.Archive;

/// <summary>
/// Unit tests for the per-chunk nonce and associated-data derivation that binds a chunk's ciphertext
/// to its content. Both values are part of the on-disk format rather than an implementation detail:
/// the nonce must stay deterministic for deduplication to work, key-dependent so identical content
/// under two passwords never reuses a nonce, and exactly
/// <see cref="EncryptionConstants.NonceSize"/> bytes long, while the associated-data layout is what
/// every previously written archive was authenticated under.
/// </summary>
public sealed class ChunkCryptoTests
{
    [Fact]
    internal void ComputeChunkNonce_SameKeyAndChunkHash_ReturnsIdenticalNonceOfExactlyNonceSize()
    {
        var nonceKey = SHA256.HashData("chunk-nonce-sub-key"u8);
        var chunkHash = SHA256.HashData("chunk plaintext"u8);

        var first = ChunkCrypto.ComputeChunkNonce(nonceKey, chunkHash);
        var second = ChunkCrypto.ComputeChunkNonce(nonceKey, chunkHash);

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

        var nonce = ChunkCrypto.ComputeChunkNonce(nonceKey, chunkHash);
        var neighbouringNonce = ChunkCrypto.ComputeChunkNonce(neighbouringKey, chunkHash);

        Assert.NotEqual(nonce, neighbouringNonce);
    }

    [Fact]
    internal void ComputeChunkNonce_ChunkHashesDifferingByOneBit_ReturnDifferentNonces()
    {
        var nonceKey = SHA256.HashData("chunk-nonce-sub-key"u8);
        var chunkHash = SHA256.HashData("chunk plaintext"u8);
        var neighbouringHash = SHA256.HashData("chunk plaintext"u8);
        neighbouringHash[0] ^= 0x01;

        var nonce = ChunkCrypto.ComputeChunkNonce(nonceKey, chunkHash);
        var neighbouringNonce = ChunkCrypto.ComputeChunkNonce(nonceKey, neighbouringHash);

        Assert.NotEqual(nonce, neighbouringNonce);
    }

    [Fact]
    internal void BuildChunkAssociatedData_ChunkHashAndNonce_ConcatenatesHashBeforeNonce()
    {
        var chunkHash = SHA256.HashData("chunk plaintext"u8);
        var nonce = ChunkCrypto.ComputeChunkNonce(
            SHA256.HashData("chunk-nonce-sub-key"u8),
            chunkHash
        );

        var associatedData = ChunkCrypto.BuildChunkAssociatedData(chunkHash, nonce);

        Assert.Multiple(
            () => Assert.Equal(chunkHash.Length + nonce.Length, associatedData.Length),
            () => Assert.Equal(chunkHash, associatedData[..chunkHash.Length]),
            () => Assert.Equal(nonce, associatedData[chunkHash.Length..])
        );
    }

    [Fact]
    internal void ComputeChunkFileName_IsAKeyedNameTheLayoutRecognizesAsAChunk()
    {
        var chunkHash = SHA256.HashData("chunk plaintext"u8);

        var fileName = ChunkCrypto.ComputeChunkFileName(SHA256.HashData("naming-key-a"u8), chunkHash);
        var otherKeyName = ChunkCrypto.ComputeChunkFileName(SHA256.HashData("naming-key-b"u8), chunkHash);

        Assert.Multiple(
            () => Assert.True(BackupLayout.IsChunkFileName(fileName)),
            () => Assert.NotEqual(fileName, otherKeyName),
            () => Assert.DoesNotContain(Convert.ToHexStringLower(chunkHash), fileName, StringComparison.Ordinal)
        );
    }
}
