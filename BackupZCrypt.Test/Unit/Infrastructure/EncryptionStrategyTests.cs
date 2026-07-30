using System.Security.Cryptography;

using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Infrastructure.Strategies.Encryption;

namespace BackupZCrypt.Test.Unit.Infrastructure;

/// <summary>
/// Unit tests for the authenticated encryption strategies.
/// </summary>
public sealed class EncryptionStrategyTests
{
    /// <summary>
    /// A fixed 32-byte key matching the production key size of <see cref="EncryptionConstants.KeySize"/>
    /// bits, shared by every cipher under test.
    /// </summary>
    private static readonly byte[] Key =
    [
        0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77,
        0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF,
        0x0F, 0x1E, 0x2D, 0x3C, 0x4B, 0x5A, 0x69, 0x78,
        0x87, 0x96, 0xA5, 0xB4, 0xC3, 0xD2, 0xE1, 0xF0,
    ];

    /// <summary>
    /// A fixed 12-byte nonce matching <see cref="EncryptionConstants.NonceSize"/>. Tests deliberately
    /// reuse it for reproducibility; production derives a distinct nonce per chunk from its hash.
    /// </summary>
    private static readonly byte[] Nonce =
    [
        0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80, 0x90, 0xA0, 0xB0, 0xC0,
    ];

    /// <summary>
    /// Arbitrary associated data bound into the authentication tag but left out of the ciphertext.
    /// </summary>
    private static readonly byte[] AssociatedData = [0xDE, 0xAD, 0xBE, 0xEF];

    /// <summary>
    /// Supplies every production AEAD cipher as a test case so all of them satisfy the same contract.
    /// </summary>
    /// <returns>One strategy instance per supported encryption algorithm.</returns>
    private static IEnumerable<IEncryptionAlgorithmStrategy> RealCiphers() =>
        [
            new AesEncryptionStrategy(),
            new ChaCha20EncryptionStrategy(),
            new CamelliaEncryptionStrategy(),
            new SerpentEncryptionStrategy(),
            new TwofishEncryptionStrategy(),
        ];

    /// <summary>
    /// Produces plaintext from a fixed seed so a failing case reproduces byte for byte.
    /// </summary>
    /// <param name="length">The number of bytes to produce.</param>
    /// <param name="seed">The seed that makes the sequence deterministic.</param>
    /// <returns>A buffer of pseudo-random bytes.</returns>
    private static byte[] RandomBytes(int length, int seed)
    {
        var data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }

    [TestCaseSource(nameof(RealCiphers))]
    public void Roundtrip_RecoversPlaintext_AcrossSizes(IEncryptionAlgorithmStrategy cipher)
    {
        int[] sizes = [0, 1, 1024, 64 * 1024];

        foreach (var size in sizes)
        {
            var plaintext = RandomBytes(size, seed: 1000 + size);

            var ciphertext = cipher.EncryptChunk(plaintext, Key, Nonce, AssociatedData);
            var decrypted = cipher.DecryptChunk(ciphertext, Key, Nonce, AssociatedData);

            Assert.That(decrypted, Is.EqualTo(plaintext));
        }
    }

    [TestCaseSource(nameof(RealCiphers))]
    public void Ciphertext_AppendsTag_AndDiffersFromPlaintext(IEncryptionAlgorithmStrategy cipher)
    {
        var plaintext = RandomBytes(1024, seed: 7);

        var ciphertext = cipher.EncryptChunk(plaintext, Key, Nonce, AssociatedData);

        Assert.That(ciphertext, Has.Length.EqualTo(plaintext.Length + EncryptionConstants.TagSize));

        var ciphertextBody = ciphertext[..plaintext.Length];
        Assert.That(ciphertextBody, Is.Not.EqualTo(plaintext));
    }

    [TestCaseSource(nameof(RealCiphers))]
    public void Decrypt_WithWrongKey_Throws(IEncryptionAlgorithmStrategy cipher)
    {
        var plaintext = RandomBytes(512, seed: 11);
        var ciphertext = cipher.EncryptChunk(plaintext, Key, Nonce, AssociatedData);

        var wrongKey = (byte[])Key.Clone();
        wrongKey[0] ^= 0xFF;

        _ = Assert.Catch<CryptographicException>(
            () => cipher.DecryptChunk(ciphertext, wrongKey, Nonce, AssociatedData)
        );
    }

    [TestCaseSource(nameof(RealCiphers))]
    public void Decrypt_WithWrongNonce_Throws(IEncryptionAlgorithmStrategy cipher)
    {
        var plaintext = RandomBytes(512, seed: 12);
        var ciphertext = cipher.EncryptChunk(plaintext, Key, Nonce, AssociatedData);

        var wrongNonce = (byte[])Nonce.Clone();
        wrongNonce[0] ^= 0xFF;

        _ = Assert.Catch<CryptographicException>(
            () => cipher.DecryptChunk(ciphertext, Key, wrongNonce, AssociatedData)
        );
    }

    [TestCaseSource(nameof(RealCiphers))]
    public void Decrypt_WithWrongAssociatedData_Throws(IEncryptionAlgorithmStrategy cipher)
    {
        var plaintext = RandomBytes(512, seed: 13);
        var ciphertext = cipher.EncryptChunk(plaintext, Key, Nonce, AssociatedData);

        var wrongAad = (byte[])AssociatedData.Clone();
        wrongAad[0] ^= 0xFF;

        _ = Assert.Catch<CryptographicException>(
            () => cipher.DecryptChunk(ciphertext, Key, Nonce, wrongAad)
        );
    }

    [TestCaseSource(nameof(RealCiphers))]
    public void Decrypt_WithTamperedCiphertext_Throws(IEncryptionAlgorithmStrategy cipher)
    {
        var plaintext = RandomBytes(512, seed: 14);
        var ciphertext = cipher.EncryptChunk(plaintext, Key, Nonce, AssociatedData);

        ciphertext[0] ^= 0x01;

        _ = Assert.Catch<CryptographicException>(
            () => cipher.DecryptChunk(ciphertext, Key, Nonce, AssociatedData)
        );
    }

    [TestCaseSource(nameof(RealCiphers))]
    public void Encrypt_WithDifferentNonce_ProducesDifferentCiphertext(
        IEncryptionAlgorithmStrategy cipher
    )
    {
        var plaintext = RandomBytes(1024, seed: 15);

        var first = cipher.EncryptChunk(plaintext, Key, Nonce, AssociatedData);

        var otherNonce = (byte[])Nonce.Clone();
        otherNonce[0] ^= 0xFF;
        var second = cipher.EncryptChunk(plaintext, Key, otherNonce, AssociatedData);

        Assert.That(second, Is.Not.EqualTo(first));
    }
}
