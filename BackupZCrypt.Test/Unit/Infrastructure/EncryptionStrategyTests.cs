using System.Security.Cryptography;
using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Infrastructure.Strategies.Encryption;

namespace BackupZCrypt.Test.Unit.Infrastructure;

public sealed class EncryptionStrategyTests
{
    // A fixed 32-byte key (256 bits) and 12-byte nonce shared across cases. Values are
    // arbitrary but deterministic so failures reproduce.
    private static readonly byte[] Key =
    [
        0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77,
        0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF,
        0x0F, 0x1E, 0x2D, 0x3C, 0x4B, 0x5A, 0x69, 0x78,
        0x87, 0x96, 0xA5, 0xB4, 0xC3, 0xD2, 0xE1, 0xF0,
    ];

    private static readonly byte[] Nonce =
    [
        0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80, 0x90, 0xA0, 0xB0, 0xC0,
    ];

    private static readonly byte[] AssociatedData = [0xDE, 0xAD, 0xBE, 0xEF];

    // The 5 ciphers that actually transform data. Indexed only by their concrete type so
    // each [Theory] case constructs a fresh instance (strategies are stateless).
    public static TheoryData<IEncryptionAlgorithmStrategy> RealCiphers() =>
        [
            new AesEncryptionStrategy(),
            new ChaCha20EncryptionStrategy(),
            new CamelliaEncryptionStrategy(),
            new SerpentEncryptionStrategy(),
            new TwofishEncryptionStrategy(),
        ];

    private static byte[] RandomBytes(int length, int seed)
    {
        var data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }

    [Theory]
    [MemberData(nameof(RealCiphers))]
    public void Roundtrip_RecoversPlaintext_AcrossSizes(IEncryptionAlgorithmStrategy cipher)
    {
        // 0 bytes, 1 byte, 1 KiB, ~64 KiB.
        int[] sizes = [0, 1, 1024, 64 * 1024];

        foreach (var size in sizes)
        {
            var plaintext = RandomBytes(size, seed: 1000 + size);

            var ciphertext = cipher.EncryptChunk(plaintext, Key, Nonce, AssociatedData);
            var decrypted = cipher.DecryptChunk(ciphertext, Key, Nonce, AssociatedData);

            Assert.Equal(plaintext, decrypted);
        }
    }

    [Theory]
    [MemberData(nameof(RealCiphers))]
    public void Ciphertext_AppendsTag_AndDiffersFromPlaintext(IEncryptionAlgorithmStrategy cipher)
    {
        var plaintext = RandomBytes(1024, seed: 7);

        var ciphertext = cipher.EncryptChunk(plaintext, Key, Nonce, AssociatedData);

        Assert.Equal(plaintext.Length + EncryptionConstants.TagSize, ciphertext.Length);

        // The leading ciphertext bytes (excluding the appended tag) must not equal the
        // plaintext: a real cipher transforms the data.
        var ciphertextBody = ciphertext[..plaintext.Length];
        Assert.NotEqual(plaintext, ciphertextBody);
    }

    [Theory]
    [MemberData(nameof(RealCiphers))]
    public void Decrypt_WithWrongKey_Throws(IEncryptionAlgorithmStrategy cipher)
    {
        var plaintext = RandomBytes(512, seed: 11);
        var ciphertext = cipher.EncryptChunk(plaintext, Key, Nonce, AssociatedData);

        var wrongKey = (byte[])Key.Clone();
        wrongKey[0] ^= 0xFF;

        // .NET's AesGcm/ChaCha20Poly1305 throw AuthenticationTagMismatchException (a
        // CryptographicException subtype), so match the base type, not the exact type.
        Assert.ThrowsAny<CryptographicException>(
            () => cipher.DecryptChunk(ciphertext, wrongKey, Nonce, AssociatedData)
        );
    }

    [Theory]
    [MemberData(nameof(RealCiphers))]
    public void Decrypt_WithWrongNonce_Throws(IEncryptionAlgorithmStrategy cipher)
    {
        var plaintext = RandomBytes(512, seed: 12);
        var ciphertext = cipher.EncryptChunk(plaintext, Key, Nonce, AssociatedData);

        var wrongNonce = (byte[])Nonce.Clone();
        wrongNonce[0] ^= 0xFF;

        Assert.ThrowsAny<CryptographicException>(
            () => cipher.DecryptChunk(ciphertext, Key, wrongNonce, AssociatedData)
        );
    }

    [Theory]
    [MemberData(nameof(RealCiphers))]
    public void Decrypt_WithWrongAssociatedData_Throws(IEncryptionAlgorithmStrategy cipher)
    {
        var plaintext = RandomBytes(512, seed: 13);
        var ciphertext = cipher.EncryptChunk(plaintext, Key, Nonce, AssociatedData);

        var wrongAad = (byte[])AssociatedData.Clone();
        wrongAad[0] ^= 0xFF;

        Assert.ThrowsAny<CryptographicException>(
            () => cipher.DecryptChunk(ciphertext, Key, Nonce, wrongAad)
        );
    }

    [Theory]
    [MemberData(nameof(RealCiphers))]
    public void Decrypt_WithTamperedCiphertext_Throws(IEncryptionAlgorithmStrategy cipher)
    {
        var plaintext = RandomBytes(512, seed: 14);
        var ciphertext = cipher.EncryptChunk(plaintext, Key, Nonce, AssociatedData);

        // Flip a bit in the body (not the appended tag) and expect AEAD verification to fail.
        ciphertext[0] ^= 0x01;

        Assert.ThrowsAny<CryptographicException>(
            () => cipher.DecryptChunk(ciphertext, Key, Nonce, AssociatedData)
        );
    }

    [Theory]
    [MemberData(nameof(RealCiphers))]
    public void Encrypt_WithDifferentNonce_ProducesDifferentCiphertext(
        IEncryptionAlgorithmStrategy cipher
    )
    {
        var plaintext = RandomBytes(1024, seed: 15);

        var first = cipher.EncryptChunk(plaintext, Key, Nonce, AssociatedData);

        var otherNonce = (byte[])Nonce.Clone();
        otherNonce[0] ^= 0xFF;
        var second = cipher.EncryptChunk(plaintext, Key, otherNonce, AssociatedData);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void None_IsPassthroughForEncryptAndDecrypt()
    {
        var strategy = new NoneEncryptionStrategy();
        var plaintext = RandomBytes(777, seed: 99);

        var encrypted = strategy.EncryptChunk(plaintext, Key, Nonce, AssociatedData);
        Assert.Equal(plaintext, encrypted);

        var decrypted = strategy.DecryptChunk(encrypted, Key, Nonce, AssociatedData);
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void None_HasNoneId()
    {
        Assert.Equal(EncryptionAlgorithm.None, new NoneEncryptionStrategy().Id);
    }
}
