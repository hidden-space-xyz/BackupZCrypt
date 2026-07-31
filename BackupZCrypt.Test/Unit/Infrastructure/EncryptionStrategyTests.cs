using System.Security.Cryptography;

using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Infrastructure.Strategies.Encryption;

namespace BackupZCrypt.Test.Unit.Infrastructure;

/// <summary>
/// Unit tests for the authenticated encryption strategies.
/// </summary>
/// <remarks>
/// Nearly every case runs against all five production ciphers. The three BouncyCastle strategies are
/// near-identical files that differ only in the engine their <c>CreateCipher</c> override creates, so
/// a copy-paste leaving one strategy wrapping another's engine is the realistic defect this fixture is
/// shaped to catch, alongside the tampering and length-guard contracts every cipher shares.
/// </remarks>
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
    /// Ciphertext lengths that cannot possibly hold an authentication tag, which is what a truncated
    /// or partially written chunk file looks like.
    /// </summary>
    /// <remarks>
    /// A chunk file cut short by an interrupted write has to fail loudly rather than yield a short
    /// buffer that a caller could mistake for plaintext.
    /// </remarks>
    private static readonly int[] UndersizedCiphertextLengths =
    [
        0, 1, 8, EncryptionConstants.TagSize - 1,
    ];

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

    [TestCaseSource(nameof(RealCiphers))]
    public void Decrypt_CiphertextShorterThanTag_ThrowsAtTheLengthBoundary(
        IEncryptionAlgorithmStrategy cipher
    )
    {
        var tagOnly = cipher.EncryptChunk(ReadOnlySpan<byte>.Empty, Key, Nonce, AssociatedData);

        using (Assert.EnterMultipleScope())
        {
            foreach (var length in UndersizedCiphertextLengths)
            {
                Assert.That(
                    () => cipher.DecryptChunk(new byte[length], Key, Nonce, AssociatedData),
                    Throws.InstanceOf<CryptographicException>(),
                    $"A {length}-byte ciphertext cannot hold a tag and must be rejected."
                );
            }

            Assert.That(
                tagOnly,
                Has.Length.EqualTo(EncryptionConstants.TagSize),
                "An empty chunk must encrypt to exactly a bare authentication tag."
            );
            Assert.That(
                cipher.DecryptChunk(tagOnly, Key, Nonce, AssociatedData),
                Is.Empty,
                "A bare tag sits one byte above the length guard and must still round-trip to an "
                    + "empty chunk."
            );
        }
    }

    [TestCaseSource(nameof(RealCiphers))]
    public void Decrypt_TamperedTagOrTruncatedBody_ThrowsForEveryMutation(
        IEncryptionAlgorithmStrategy cipher
    )
    {
        var plaintext = RandomBytes(512, seed: 31);
        var ciphertext = cipher.EncryptChunk(plaintext, Key, Nonce, AssociatedData);

        var lastTagByteFlipped = (byte[])ciphertext.Clone();
        lastTagByteFlipped[^1] ^= 0x01;

        var firstTagByteFlipped = (byte[])ciphertext.Clone();
        firstTagByteFlipped[^EncryptionConstants.TagSize] ^= 0x01;

        var bodyLength = ciphertext.Length - EncryptionConstants.TagSize;
        var truncatedBody = new byte[ciphertext.Length - 1];
        ciphertext.AsSpan(0, bodyLength - 1).CopyTo(truncatedBody);
        ciphertext.AsSpan(bodyLength).CopyTo(truncatedBody.AsSpan(bodyLength - 1));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                () => cipher.DecryptChunk(lastTagByteFlipped, Key, Nonce, AssociatedData),
                Throws.InstanceOf<CryptographicException>(),
                "A flipped final tag byte was accepted. This case exercises the tag comparison "
                    + "itself, rather than the decrypt-then-verify path the body-tampering test "
                    + "covers."
            );
            Assert.That(
                () => cipher.DecryptChunk(firstTagByteFlipped, Key, Nonce, AssociatedData),
                Throws.InstanceOf<CryptographicException>(),
                "A flipped first tag byte was accepted."
            );
            Assert.That(
                () => cipher.DecryptChunk(truncatedBody, Key, Nonce, AssociatedData),
                Throws.InstanceOf<CryptographicException>(),
                "A ciphertext body missing one byte was accepted. It is long enough to clear every "
                    + "length guard, so only authentication can reject it."
            );
        }
    }

    [TestCaseSource(nameof(RealCiphers))]
    public void Encrypt_EmptyNonce_ThrowsArgumentException(IEncryptionAlgorithmStrategy cipher)
    {
        Assert.That(
            () => cipher.EncryptChunk(RandomBytes(64, seed: 51), Key, [], AssociatedData),
            Throws.InstanceOf<ArgumentException>(),
            "A degenerate nonce is catastrophic for AEAD and was accepted instead of throwing. Only "
                + "the empty nonce is asserted across all five ciphers: the platform primitives "
                + "reject an 11-byte nonce while BouncyCastle's GCM accepts any IV length, so a "
                + "shared undersized-nonce case would codify that divergence as the contract."
        );
    }

    [TestCaseSource(nameof(RealCiphers))]
    public void EncryptAndDecrypt_UndersizedKey_ThrowInsteadOfPaddingTheKey(
        IEncryptionAlgorithmStrategy cipher
    )
    {
        var undersizedKey = new byte[(EncryptionConstants.KeySize / 8) - 1];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                () =>
                    cipher.EncryptChunk(
                        RandomBytes(64, seed: 61),
                        undersizedKey,
                        Nonce,
                        AssociatedData
                    ),
                Throws.InstanceOf<Exception>(),
                "Encryption accepted a 31-byte key, which is invalid for all five ciphers. A silent "
                    + "truncation or zero-pad would quietly degrade the 256-bit key promise; the "
                    + "concrete exception type differs per implementation, so only the refusal "
                    + "itself is asserted."
            );
            Assert.That(
                () => cipher.DecryptChunk(new byte[80], undersizedKey, Nonce, AssociatedData),
                Throws.InstanceOf<Exception>(),
                "Decryption accepted a 31-byte key."
            );
        }
    }

    [TestCaseSource(nameof(RealCiphers))]
    public void Encrypt_SameKeyNonceAndPlaintext_ProducesIdenticalCiphertext(
        IEncryptionAlgorithmStrategy cipher
    )
    {
        var plaintext = RandomBytes(1024, seed: 71);

        var first = cipher.EncryptChunk(plaintext, Key, Nonce, AssociatedData);
        var second = cipher.EncryptChunk(plaintext, Key, Nonce, AssociatedData);

        Assert.That(
            second,
            Is.EqualTo(first),
            "The same key, nonce and plaintext produced different ciphertexts. Deduplication "
                + "depends on encryption being a pure function of those three inputs - the "
                + "per-chunk nonce is derived from the chunk hash precisely so identical content "
                + "stores identical bytes - and internal randomness here makes every update rewrite "
                + "the whole backup while every round-trip test still passes."
        );
    }

    [Test]
    public void Encrypt_SameInputsThroughEveryCipher_ProducesADistinctCiphertextPerAlgorithm()
    {
        var plaintext = RandomBytes(64, seed: 81);

        var ciphertexts = RealCiphers()
            .Select(cipher =>
                Convert.ToHexString(cipher.EncryptChunk(plaintext, Key, Nonce, AssociatedData))
            )
            .ToArray();

        Assert.That(
            ciphertexts,
            Is.Unique,
            "Two encryption strategies turned the same key, nonce and plaintext into the same "
                + "ciphertext, so they are wired to the same engine. Identical output is the only "
                + "observable symptom: round-trip, tamper detection and determinism all still hold, "
                + "the composition tests still see the right Id, and the archive is written with a "
                + "cipher the manifest does not name."
        );
    }
}
