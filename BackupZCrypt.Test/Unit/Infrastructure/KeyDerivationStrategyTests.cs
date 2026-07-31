using System.Security.Cryptography;

using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Infrastructure.Strategies.KeyDerivation;

namespace BackupZCrypt.Test.Unit.Infrastructure;

/// <summary>
/// Unit tests for the key-derivation strategies (Argon2id, PBKDF2, and scrypt).
/// </summary>
/// <remarks>
/// Each strategy wraps its provider's failures in a <see cref="CryptographicException"/>, the only
/// exception type the layers above expect from a key derivation: a raw provider exception escaping is
/// mapped to "unexpected error" instead of a password or KDF problem, and the inner exception has to
/// survive because a derivation failure is otherwise undiagnosable from the message alone. A null
/// password is simply the cheapest way to make a derivation fail - it fails on the first statement
/// inside the guarded block, so not a single round of PBKDF2, scrypt or Argon2id is computed.
/// </remarks>
public sealed class KeyDerivationStrategyTests
{
    /// <summary>
    /// The passphrase every derivation starts from; its exact value is irrelevant beyond being fixed.
    /// </summary>
    private const string Password = "correct horse battery staple";

    /// <summary>
    /// The requested key length in bits, taken from the production master-key size.
    /// </summary>
    private const int KeySizeBits = EncryptionConstants.KeySize;

    /// <summary>
    /// A fixed 32-byte salt matching <see cref="EncryptionConstants.SaltSize"/>, hard-coded so
    /// derivations repeat exactly across runs.
    /// </summary>
    private static readonly byte[] Salt =
    [
        0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
        0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10,
        0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18,
        0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F, 0x20,
    ];

    /// <summary>
    /// Supplies every production key-derivation function as a test case so all of them satisfy the
    /// same contract.
    /// </summary>
    /// <returns>One strategy instance per supported key-derivation algorithm.</returns>
    private static IEnumerable<IKeyDerivationAlgorithmStrategy> Kdfs() =>
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

        Assert.That(key, Has.Length.EqualTo(KeySizeBits / 8));

        var keyAgain = kdf.DeriveKey(Password, Salt, KeySizeBits);
        Assert.That(keyAgain, Is.EqualTo(key));

        var otherSalt = (byte[])Salt.Clone();
        otherSalt[0] ^= 0xFF;
        var keyOtherSalt = kdf.DeriveKey(Password, otherSalt, KeySizeBits);
        Assert.That(keyOtherSalt, Is.Not.EqualTo(key));

        var keyOtherPassword = kdf.DeriveKey(Password + "!", Salt, KeySizeBits);
        Assert.That(keyOtherPassword, Is.Not.EqualTo(key));
    }

    [TestCaseSource(nameof(Kdfs))]
    public void DeriveKey_WhenDerivationFails_ReportsACryptographicExceptionThatKeepsTheCause(
        IKeyDerivationAlgorithmStrategy kdf
    )
    {
        var error = Assert.Throws<CryptographicException>(
            () => kdf.DeriveKey(null!, Salt, KeySizeBits)
        );

        Assert.That(
            error?.InnerException,
            Is.Not.Null,
            "The provider failure was swallowed, leaving nothing to diagnose the derivation with."
        );
    }
}
