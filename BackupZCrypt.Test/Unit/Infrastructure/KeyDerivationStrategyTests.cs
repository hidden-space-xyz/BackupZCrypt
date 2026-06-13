using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Infrastructure.Strategies.KeyDerivation;

namespace BackupZCrypt.Test.Unit.Infrastructure;

// Argon2id is memory-heavy (~256 MB) and slow, so the determinism, salt-sensitivity and
// password-sensitivity checks are folded into a single theory: each KDF runs DeriveKey
// only four times total (and Argon2id is never placed inside a larger data-driven loop).
public sealed class KeyDerivationStrategyTests
{
    private const string Password = "correct horse battery staple";
    private const int KeySizeBits = EncryptionConstants.KeySize; // 256

    private static readonly byte[] Salt =
    [
        0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
        0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10,
        0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18,
        0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F, 0x20,
    ];

    public static IEnumerable<IKeyDerivationAlgorithmStrategy> Kdfs() =>
        [
            new Argon2IdKeyDerivationStrategy(),
            new Pbkdf2KeyDerivationStrategy(),
            new ScryptKeyDerivationStrategy(),
        ];

    [TestCaseSource(nameof(Kdfs))]
    public void DeriveKey_DeterministicCorrectLength_AndSensitiveToSaltAndPassword(
        IKeyDerivationAlgorithmStrategy kdf
    )
    {
        var key = kdf.DeriveKey(Password, Salt, KeySizeBits);

        // Correct output length: keySize is in bits, the key is keySize/8 bytes.
        Assert.That(key.Length, Is.EqualTo(KeySizeBits / 8));

        // Determinism: identical inputs yield byte-identical output.
        var keyAgain = kdf.DeriveKey(Password, Salt, KeySizeBits);
        Assert.That(keyAgain, Is.EqualTo(key));

        // Salt sensitivity: flipping one salt byte changes the key.
        var otherSalt = (byte[])Salt.Clone();
        otherSalt[0] ^= 0xFF;
        var keyOtherSalt = kdf.DeriveKey(Password, otherSalt, KeySizeBits);
        Assert.That(keyOtherSalt, Is.Not.EqualTo(key));

        // Password sensitivity: a different password changes the key.
        var keyOtherPassword = kdf.DeriveKey(Password + "!", Salt, KeySizeBits);
        Assert.That(keyOtherPassword, Is.Not.EqualTo(key));
    }
}
