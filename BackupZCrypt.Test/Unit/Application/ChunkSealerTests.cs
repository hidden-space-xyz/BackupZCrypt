using System.Security.Cryptography;

using BackupZCrypt.Application.Engine;
using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Infrastructure.Strategies.Compression;
using BackupZCrypt.Infrastructure.Strategies.Encryption;
using BackupZCrypt.Test.Tools;

using NSubstitute;

namespace BackupZCrypt.Test.Unit.Application;

/// <summary>
/// Unit tests for <see cref="ChunkSealer"/>, the single implementation of the stored chunk layout
/// shared by the writer, the reader, and the benchmark.
/// </summary>
/// <remarks>
/// The committed format fixtures pin what the reader accepts; the known-answer case here pins what
/// the writer produces, computed independently with the platform's AES-GCM. The remaining cases pin
/// the guards around the layout: what a reader refuses before it allocates, what it refuses after it
/// authenticates, that nothing reaches the destination from a chunk that fails authentication, and
/// that the derived values and intermediate copies are wiped.
/// </remarks>
public sealed class ChunkSealerTests
{
    /// <summary>
    /// The plaintext length of the sample chunk, small enough to keep the tests fast and large
    /// enough to compress.
    /// </summary>
    private const int SampleSize = 64 * 1024;

    /// <summary>
    /// The largest compressed chunk the writer may seal: the reader's limit less the tag.
    /// </summary>
    private const int LargestSealableCompressedSize =
        EngineLimits.MaximumStoredChunkSize - EncryptionConstants.TagSize;

    /// <summary>
    /// Gets both storage modes, so every round-trip case runs with and without compression.
    /// </summary>
    public static TheoryData<bool> CompressionModes => new(false, true);

    [Fact]
    internal async Task Seal_Uncompressed_MatchesAnIndependentlyComputedCiphertext()
    {
        var key = CreateFixedKey(0x10);
        var nonceKey = CreateFixedKey(0x40);
        var chunk = OnDiskFormatFixtures.GoldenChunkContent;
        var chunkHash = SHA256.HashData(chunk);
        ChunkSealer sealer = new(key, nonceKey, new AesEncryptionStrategy(), null);

        var stored = await sealer.SealAsync(chunk, chunkHash, CancellationToken.None);

        var nonce = HMACSHA256.HashData(nonceKey, chunkHash)[..EncryptionConstants.NonceSize];
        byte[] associatedData = [.. chunkHash, .. nonce];
        var ciphertext = new byte[chunk.Length];
        var tag = new byte[EncryptionConstants.TagSize];
        using (AesGcm aes = new(key, EncryptionConstants.TagSize))
        {
            aes.Encrypt(nonce, chunk, ciphertext, tag, associatedData);
        }

        Assert.Equal([.. ciphertext, .. tag], stored);
    }

    [Theory]
    [MemberData(nameof(CompressionModes))]
    internal async Task SealThenOpen_ReturnsTheOriginalPlaintextAndHash(bool compressed)
    {
        var sealer = CreateSealer(compressed);
        var chunk = CreateSample();
        var chunkHash = SHA256.HashData(chunk);

        var stored = await sealer.SealAsync(chunk, chunkHash, CancellationToken.None);

        await using MemoryStream destination = new();
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var opened = await sealer.OpenAsync(
            stored,
            chunkHash,
            chunk.Length,
            destination,
            hasher,
            CancellationToken.None
        );

        Assert.Equal(chunk.Length, opened);
        Assert.Equal(chunk, destination.ToArray());
        Assert.Equal(chunkHash, hasher.GetHashAndReset());
        Assert.True(sealer.IsPlausibleStoredSize(stored.Length, chunk.Length));
    }

    [Theory]
    [MemberData(nameof(CompressionModes))]
    internal async Task Seal_SameChunkTwice_ProducesIdenticalBytesSoDeduplicationHolds(bool compressed)
    {
        var sealer = CreateSealer(compressed);
        var chunk = CreateSample();
        var chunkHash = SHA256.HashData(chunk);

        var first = await sealer.SealAsync(chunk, chunkHash, CancellationToken.None);
        var second = await sealer.SealAsync(chunk, chunkHash, CancellationToken.None);

        Assert.Equal(first, second);
    }

    [Fact]
    internal async Task Seal_Uncompressed_StoresExactlyThePlaintextPlusTheTag()
    {
        var sealer = CreateSealer(compressed: false);
        var chunk = CreateSample();

        var stored = await sealer.SealAsync(chunk, SHA256.HashData(chunk), CancellationToken.None);

        Assert.Equal(chunk.Length + EncryptionConstants.TagSize, stored.Length);
        Assert.Equal(stored.Length, sealer.MaximumStoredSize(chunk.Length));
    }

    [Fact]
    internal async Task Seal_Compressed_StoresLessThanThePlaintextOfCompressibleData()
    {
        var sealer = CreateSealer(compressed: true);
        var chunk = CreateSample();

        var stored = await sealer.SealAsync(chunk, SHA256.HashData(chunk), CancellationToken.None);

        Assert.True(stored.Length < chunk.Length);
        Assert.Equal(EngineLimits.MaximumStoredChunkSize, sealer.MaximumStoredSize(chunk.Length));
    }

    [Theory]
    [MemberData(nameof(CompressionModes))]
    internal async Task Open_TamperedChunk_FailsAuthenticationAndWritesNothing(bool compressed)
    {
        var sealer = CreateSealer(compressed);
        var chunk = CreateSample();
        var chunkHash = SHA256.HashData(chunk);
        var stored = await sealer.SealAsync(chunk, chunkHash, CancellationToken.None);
        stored[stored.Length / 2] ^= 0x01;

        await using MemoryStream destination = new();
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        _ = await Assert.ThrowsAnyAsync<CryptographicException>(
            () => sealer.OpenAsync(stored, chunkHash, chunk.Length, destination, hasher, CancellationToken.None)
        );
        Assert.Equal(0, destination.Length);
    }

    [Theory]
    [MemberData(nameof(CompressionModes))]
    internal async Task Open_HashOfAnotherChunk_FailsAuthenticationAndWritesNothing(bool compressed)
    {
        var sealer = CreateSealer(compressed);
        var chunk = CreateSample();
        var stored = await sealer.SealAsync(chunk, SHA256.HashData(chunk), CancellationToken.None);
        var otherHash = SHA256.HashData([1, 2, 3]);

        await using MemoryStream destination = new();
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        _ = await Assert.ThrowsAnyAsync<CryptographicException>(
            () => sealer.OpenAsync(stored, otherHash, chunk.Length, destination, hasher, CancellationToken.None)
        );
        Assert.Equal(0, destination.Length);
    }

    [Theory]
    [MemberData(nameof(CompressionModes))]
    internal async Task Open_DeclaredSizeLargerThanThePlaintext_IsRejectedAfterAuthentication(bool compressed)
    {
        var sealer = CreateSealer(compressed);
        var chunk = CreateSample();
        var chunkHash = SHA256.HashData(chunk);
        var stored = await sealer.SealAsync(chunk, chunkHash, CancellationToken.None);

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        _ = await Assert.ThrowsAsync<CryptographicException>(
            () => sealer.OpenAsync(stored, chunkHash, chunk.Length + 1, Stream.Null, hasher, CancellationToken.None)
        );
    }

    [Fact]
    internal async Task Open_Uncompressed_DeclaredSizeSmallerThanThePlaintext_IsRejectedAfterAuthentication()
    {
        var sealer = CreateSealer(compressed: false);
        var chunk = CreateSample();
        var chunkHash = SHA256.HashData(chunk);
        var stored = await sealer.SealAsync(chunk, chunkHash, CancellationToken.None);

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        _ = await Assert.ThrowsAsync<CryptographicException>(
            () => sealer.OpenAsync(stored, chunkHash, chunk.Length - 1, Stream.Null, hasher, CancellationToken.None)
        );
    }

    [Fact]
    internal async Task Open_Compressed_ExpandingPastTheDeclaredSize_StopsDecompression()
    {
        var sealer = CreateSealer(compressed: true);
        var chunk = CreateSample();
        var chunkHash = SHA256.HashData(chunk);
        var stored = await sealer.SealAsync(chunk, chunkHash, CancellationToken.None);

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        _ = await Assert.ThrowsAsync<InvalidDataException>(
            () => sealer.OpenAsync(stored, chunkHash, chunk.Length - 1, Stream.Null, hasher, CancellationToken.None)
        );
    }

    [Fact]
    internal void IsPlausibleStoredSize_Uncompressed_AcceptsOnlyTheExactLength()
    {
        var sealer = CreateSealer(compressed: false);
        var exact = SampleSize + EncryptionConstants.TagSize;

        Assert.True(sealer.IsPlausibleStoredSize(exact, SampleSize));
        Assert.False(sealer.IsPlausibleStoredSize(exact - 1, SampleSize));
        Assert.False(sealer.IsPlausibleStoredSize(exact + 1, SampleSize));
    }

    [Fact]
    internal void IsPlausibleStoredSize_Compressed_AcceptsAnyLengthBetweenTheTagAndTheLimit()
    {
        var sealer = CreateSealer(compressed: true);

        Assert.False(sealer.IsPlausibleStoredSize(EncryptionConstants.TagSize - 1, SampleSize));
        Assert.True(sealer.IsPlausibleStoredSize(EncryptionConstants.TagSize, SampleSize));
        Assert.True(sealer.IsPlausibleStoredSize(EngineLimits.MaximumStoredChunkSize, SampleSize));
        Assert.False(sealer.IsPlausibleStoredSize(EngineLimits.MaximumStoredChunkSize + 1L, SampleSize));
    }

    [Fact]
    internal void MaximumStoredSize_Uncompressed_ChunkSizeAtTheIntegerLimit_Overflows()
    {
        var sealer = CreateSealer(compressed: false);

        _ = Assert.Throws<OverflowException>(() => sealer.MaximumStoredSize(int.MaxValue));
    }

    [Fact]
    internal async Task Seal_CompressionAtExactlyTheLimit_IsSealedAndAcceptedByTheReader()
    {
        var compression = SubstituteCompression(new byte[LargestSealableCompressedSize], asMemoryStream: true);
        ChunkSealer sealer = new(CreateRandomKey(), CreateRandomKey(), new AesEncryptionStrategy(), compression);
        var chunk = CreateSample();

        var stored = await sealer.SealAsync(chunk, SHA256.HashData(chunk), CancellationToken.None);

        Assert.Equal(EngineLimits.MaximumStoredChunkSize, stored.Length);
        Assert.True(sealer.IsPlausibleStoredSize(stored.Length, chunk.Length));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    internal async Task Seal_CompressionLargerThanTheReaderAccepts_IsRefused(bool asMemoryStream)
    {
        var compression = SubstituteCompression(new byte[LargestSealableCompressedSize + 1], asMemoryStream);
        ChunkSealer sealer = new(CreateRandomKey(), CreateRandomKey(), new AesEncryptionStrategy(), compression);
        var chunk = CreateSample();

        _ = await Assert.ThrowsAsync<InvalidDataException>(
            () => sealer.SealAsync(chunk, SHA256.HashData(chunk), CancellationToken.None)
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    internal async Task Seal_CompressionReturningAnyStreamType_EncryptsItsWholeOutput(bool asMemoryStream)
    {
        byte[] compressedBytes = [9, 8, 7, 6, 5, 4, 3, 2, 1];
        var compression = SubstituteCompression(compressedBytes, asMemoryStream);
        ChunkSealer sealer = new(CreateRandomKey(), CreateRandomKey(), new AesEncryptionStrategy(), compression);
        var chunk = CreateSample();

        var stored = await sealer.SealAsync(chunk, SHA256.HashData(chunk), CancellationToken.None);

        Assert.Equal(compressedBytes.Length + EncryptionConstants.TagSize, stored.Length);
    }

    [Fact]
    internal async Task Seal_WipesTheNonceTheAssociatedDataAndTheCompressionBuffer()
    {
        MemoryStream? compressedOutput = null;
        var compression = Substitute.For<ICompressionStrategy>();
        _ = compression
            .CompressAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                compressedOutput = new MemoryStream();
                compressedOutput.Write([1, 2, 3, 4, 5, 6, 7, 8]);
                compressedOutput.Position = 0;
                return Task.FromResult<Stream>(compressedOutput);
            });
        RecordingEncryption encryption = new();
        ChunkSealer sealer = new(CreateRandomKey(), CreateRandomKey(), encryption, compression);
        var chunk = CreateSample();

        _ = await sealer.SealAsync(chunk, SHA256.HashData(chunk), CancellationToken.None);

        Assert.NotNull(compressedOutput);
        Assert.All(compressedOutput.GetBuffer().AsSpan(0, 8).ToArray(), static b => Assert.Equal(0, b));
        AssertAllWiped(encryption.Nonces);
        AssertAllWiped(encryption.AssociatedData);
    }

    [Theory]
    [MemberData(nameof(CompressionModes))]
    internal async Task Open_Success_WipesTheDecryptedChunkTheNonceAndTheAssociatedData(bool compressed)
    {
        RecordingEncryption encryption = new();
        ChunkSealer sealer = new(
            CreateRandomKey(),
            CreateRandomKey(),
            encryption,
            compressed ? new ZstdCompressionStrategy() : null
        );
        var chunk = CreateSample();
        var chunkHash = SHA256.HashData(chunk);
        var stored = await sealer.SealAsync(chunk, chunkHash, CancellationToken.None);
        encryption.Clear();

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        _ = await sealer.OpenAsync(stored, chunkHash, chunk.Length, Stream.Null, hasher, CancellationToken.None);

        _ = Assert.Single(encryption.Decrypted);
        AssertAllWiped(encryption.Decrypted);
        AssertAllWiped(encryption.Nonces);
        AssertAllWiped(encryption.AssociatedData);
    }

    [Fact]
    internal async Task Open_SizeMismatchAfterAuthentication_StillWipesTheDecryptedChunk()
    {
        RecordingEncryption encryption = new();
        ChunkSealer sealer = new(CreateRandomKey(), CreateRandomKey(), encryption, null);
        var chunk = CreateSample();
        var chunkHash = SHA256.HashData(chunk);
        var stored = await sealer.SealAsync(chunk, chunkHash, CancellationToken.None);
        encryption.Clear();

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        _ = await Assert.ThrowsAsync<CryptographicException>(
            () => sealer.OpenAsync(stored, chunkHash, chunk.Length - 1, Stream.Null, hasher, CancellationToken.None)
        );

        _ = Assert.Single(encryption.Decrypted);
        AssertAllWiped(encryption.Decrypted);
        AssertAllWiped(encryption.Nonces);
        AssertAllWiped(encryption.AssociatedData);
    }

    [Fact]
    internal async Task Open_FailedAuthentication_StillWipesTheNonceAndTheAssociatedData()
    {
        RecordingEncryption encryption = new();
        ChunkSealer sealer = new(CreateRandomKey(), CreateRandomKey(), encryption, null);
        var chunk = CreateSample();
        var chunkHash = SHA256.HashData(chunk);
        var stored = await sealer.SealAsync(chunk, chunkHash, CancellationToken.None);
        stored[0] ^= 0x01;
        encryption.Clear();

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        _ = await Assert.ThrowsAnyAsync<CryptographicException>(
            () => sealer.OpenAsync(stored, chunkHash, chunk.Length, Stream.Null, hasher, CancellationToken.None)
        );

        Assert.NotEmpty(encryption.Nonces);
        AssertAllWiped(encryption.Nonces);
        AssertAllWiped(encryption.AssociatedData);
    }

    /// <summary>
    /// Asserts that every recorded buffer holds only zero bytes.
    /// </summary>
    /// <param name="buffers">The recorded buffers.</param>
    private static void AssertAllWiped(IEnumerable<byte[]> buffers)
    {
        Assert.All(buffers, static buffer => Assert.All(buffer, static b => Assert.Equal(0, b)));
    }

    /// <summary>
    /// Builds a sealer over AES-GCM with fresh random keys, optionally compressing with Zstandard.
    /// </summary>
    /// <param name="compressed">Whether chunks are compressed before encryption.</param>
    /// <returns>The sealer.</returns>
    private static ChunkSealer CreateSealer(bool compressed)
    {
        return new ChunkSealer(
            CreateRandomKey(),
            CreateRandomKey(),
            new AesEncryptionStrategy(),
            compressed ? new ZstdCompressionStrategy() : null
        );
    }

    /// <summary>
    /// Creates a random key of the production key size.
    /// </summary>
    /// <returns>The key.</returns>
    private static byte[] CreateRandomKey()
    {
        return RandomNumberGenerator.GetBytes(EncryptionConstants.KeySize / 8);
    }

    /// <summary>
    /// Creates a fixed key of the production key size whose bytes count up from a seed.
    /// </summary>
    /// <param name="seed">The value of the first byte.</param>
    /// <returns>The key.</returns>
    private static byte[] CreateFixedKey(byte seed)
    {
        var key = new byte[EncryptionConstants.KeySize / 8];

        for (var i = 0; i < key.Length; i++)
        {
            key[i] = (byte)(seed + i);
        }

        return key;
    }

    /// <summary>
    /// Creates a deterministic, partially compressible sample chunk.
    /// </summary>
    /// <returns>The sample.</returns>
    private static byte[] CreateSample()
    {
        var sample = new byte[SampleSize];

        for (var i = 0; i < sample.Length; i++)
        {
            sample[i] = (byte)(i % 251 < 128 ? i % 7 : i % 251);
        }

        return sample;
    }

    /// <summary>
    /// Creates a compression strategy that ignores its input and yields fixed bytes, either as a
    /// <see cref="MemoryStream"/> or wrapped in another stream type.
    /// </summary>
    /// <param name="output">The bytes the strategy yields.</param>
    /// <param name="asMemoryStream">Whether the yielded stream is a <see cref="MemoryStream"/>.</param>
    /// <returns>The substitute strategy.</returns>
    private static ICompressionStrategy SubstituteCompression(byte[] output, bool asMemoryStream)
    {
        var compression = Substitute.For<ICompressionStrategy>();
        _ = compression
            .CompressAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                MemoryStream memory = new(output.ToArray(), writable: false);
                return Task.FromResult<Stream>(asMemoryStream ? memory : new BufferedStream(memory));
            });

        return compression;
    }

    /// <summary>
    /// An AES-GCM strategy that keeps a reference to every nonce and associated-data array it is
    /// handed and every plaintext array it returns, so a test can check they were wiped afterwards.
    /// </summary>
    private sealed class RecordingEncryption : IEncryptionAlgorithmStrategy
    {
        /// <summary>
        /// The real cipher the calls are forwarded to.
        /// </summary>
        private readonly AesEncryptionStrategy inner = new();

        /// <summary>
        /// Gets the nonce arrays the sealer passed in.
        /// </summary>
        public List<byte[]> Nonces { get; } = [];

        /// <summary>
        /// Gets the associated-data arrays the sealer passed in.
        /// </summary>
        public List<byte[]> AssociatedData { get; } = [];

        /// <summary>
        /// Gets the plaintext arrays handed back to the sealer by decryption.
        /// </summary>
        public List<byte[]> Decrypted { get; } = [];

        /// <inheritdoc/>
        public EncryptionAlgorithm Id => this.inner.Id;

        /// <summary>
        /// Forgets every recorded array.
        /// </summary>
        public void Clear()
        {
            this.Nonces.Clear();
            this.AssociatedData.Clear();
            this.Decrypted.Clear();
        }

        /// <inheritdoc/>
        public byte[] EncryptChunk(
            ReadOnlySpan<byte> plaintext,
            byte[] key,
            byte[] nonce,
            byte[] associatedData
        )
        {
            this.Nonces.Add(nonce);
            this.AssociatedData.Add(associatedData);

            return this.inner.EncryptChunk(plaintext, key, nonce, associatedData);
        }

        /// <inheritdoc/>
        public byte[] DecryptChunk(
            ReadOnlySpan<byte> ciphertext,
            byte[] key,
            byte[] nonce,
            byte[] associatedData
        )
        {
            this.Nonces.Add(nonce);
            this.AssociatedData.Add(associatedData);

            var plaintext = this.inner.DecryptChunk(ciphertext, key, nonce, associatedData);
            this.Decrypted.Add(plaintext);

            return plaintext;
        }
    }
}
