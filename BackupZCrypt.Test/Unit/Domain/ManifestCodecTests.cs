using System.Security.Cryptography;

using BackupZCrypt.Domain.Archive;
using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Infrastructure.Strategies.Encryption;

namespace BackupZCrypt.Test.Unit.Domain;

/// <summary>
/// Unit tests for <see cref="ManifestCodec"/>, the byte layout of the manifest file, exercised
/// without any file access. The committed archives in the format tests pin the layout against what
/// earlier versions wrote; these tests pin how the codec treats input it must refuse.
/// </summary>
public sealed class ManifestCodecTests
{
    /// <summary>
    /// The AES strategy every manifest in these tests is encrypted with.
    /// </summary>
    private readonly AesEncryptionStrategy aes = new();

    [Fact]
    internal void Encrypt_ThenReadPreambleAndDecrypt_RoundTripsTheManifest()
    {
        var salt = SaltOf(7);
        var key = KeyOf(9);
        var manifest = Manifest(salt);

        var file = ManifestCodec.Encrypt(manifest, salt, key, EncryptionAlgorithm.Aes, this.aes);
        var preamble = ManifestCodec.ReadPreamble(file);
        var decrypted = ManifestCodec.Decrypt(preamble!, key, this.aes);

        Assert.Multiple(
            () => Assert.Equal((byte)EncryptionAlgorithm.Aes, file[0]),
            () => Assert.Equal((byte)KeyDerivationAlgorithm.PBKDF2, file[1]),
            () => Assert.Equal(salt, file[2..ManifestCodec.PreambleHeaderSize]),
            () => Assert.Equal(manifest.Header, decrypted!.Header),
            () => Assert.Equal(manifest.MasterSalt, decrypted!.MasterSalt),
            () => Assert.Equal("a.txt", Assert.Single(decrypted!.Files).OriginalPath)
        );
    }

    [Fact]
    internal void ReadPreamble_FileShorterThanAPreambleAndNonce_IsRejected()
    {
        var file = new byte[ManifestCodec.PreambleHeaderSize + EncryptionConstants.NonceSize - 1];

        _ = Assert.Throws<InvalidDataException>(() => ManifestCodec.ReadPreamble(file));
    }

    [Theory]
    [InlineData(99, (byte)KeyDerivationAlgorithm.PBKDF2)]
    [InlineData((byte)EncryptionAlgorithm.Aes, 99)]
    internal void ReadPreamble_UnknownAlgorithmIdentifier_ReturnsNull(byte algorithm, byte keyDerivation)
    {
        var file = new byte[ManifestCodec.MinimumManifestSize];
        file[0] = algorithm;
        file[1] = keyDerivation;

        Assert.Null(ManifestCodec.ReadPreamble(file));
    }

    [Fact]
    internal void Decrypt_PreambleNamingAnUnknownAlgorithm_ReturnsNull()
    {
        var salt = SaltOf(3);
        var key = KeyOf(4);
        var preamble = ManifestCodec.ReadPreamble(
            ManifestCodec.Encrypt(Manifest(salt), salt, key, EncryptionAlgorithm.Aes, this.aes)
        )!;

        Assert.Multiple(
            () => Assert.Null(
                ManifestCodec.Decrypt(preamble with { Algorithm = (EncryptionAlgorithm)99 }, key, this.aes)
            ),
            () => Assert.Null(
                ManifestCodec.Decrypt(preamble with { KeyDerivation = (KeyDerivationAlgorithm)99 }, key, this.aes)
            )
        );
    }

    [Fact]
    internal void Encrypt_MasterSaltOfTheWrongLength_IsRejected()
    {
        var salt = SaltOf(5);

        _ = Assert.Throws<ArgumentException>(
            () => ManifestCodec.Encrypt(
                Manifest(salt),
                salt[..^1],
                KeyOf(6),
                EncryptionAlgorithm.Aes,
                this.aes
            )
        );
    }

    /// <summary>
    /// Builds a one-file manifest recorded under the given master salt.
    /// </summary>
    /// <param name="salt">The master salt the manifest records.</param>
    /// <returns>The manifest.</returns>
    private static ChunkManifestData Manifest(byte[] salt)
    {
        var hash = Convert.ToBase64String(SHA256.HashData("a"u8));

        return new ChunkManifestData(
            new ManifestHeader(EncryptionAlgorithm.Aes, KeyDerivationAlgorithm.PBKDF2, CompressionMode.None),
            Convert.ToBase64String(salt),
            [
                new ChunkManifestFileEntry(
                    "a.txt",
                    hash,
                    1,
                    [new ChunkManifestChunkRef(hash, 1)],
                    new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                ),
            ]
        );
    }

    /// <summary>
    /// Builds a deterministic master salt.
    /// </summary>
    /// <param name="seed">The byte every position of the salt holds.</param>
    /// <returns>A salt of <see cref="EncryptionConstants.SaltSize"/> bytes.</returns>
    private static byte[] SaltOf(byte seed)
    {
        return Enumerable.Repeat(seed, EncryptionConstants.SaltSize).ToArray();
    }

    /// <summary>
    /// Builds a deterministic 256-bit key.
    /// </summary>
    /// <param name="seed">The byte every position of the key holds.</param>
    /// <returns>A key of <see cref="EncryptionConstants.KeySize"/> bits.</returns>
    private static byte[] KeyOf(byte seed)
    {
        return Enumerable.Repeat(seed, EncryptionConstants.KeySize / 8).ToArray();
    }
}
