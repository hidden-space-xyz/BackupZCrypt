namespace BackupZCrypt.Test.Infrastructure.Strategies.KeyDerivation;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Infrastructure.Strategies.KeyDerivation;
using System.Security.Cryptography;

[TestFixtureSource(nameof(Strategies))]
internal sealed class KeyDerivationStrategyTests(
    IKeyDerivationAlgorithmStrategy strategy,
    KeyDerivationAlgorithm expectedId)
{
    private static IEnumerable<TestFixtureData> Strategies()
    {
        yield return new TestFixtureData(new Argon2IdKeyDerivationStrategy(), KeyDerivationAlgorithm.Argon2id)
            .SetArgDisplayNames("Argon2Id");
        yield return new TestFixtureData(new Pbkdf2KeyDerivationStrategy(), KeyDerivationAlgorithm.PBKDF2)
            .SetArgDisplayNames("PBKDF2");
        yield return new TestFixtureData(new ScryptKeyDerivationStrategy(), KeyDerivationAlgorithm.Scrypt)
            .SetArgDisplayNames("Scrypt");
    }

    [Test]
    public void Id_ReturnsExpected()
    {
        Assert.That(strategy.Id, Is.EqualTo(expectedId));
    }

    [Test]
    public void DisplayName_IsNotEmpty()
    {
        Assert.That(strategy.DisplayName, Is.Not.Empty);
    }

    [Test]
    public void Description_IsNotEmpty()
    {
        Assert.That(strategy.Description, Is.Not.Empty);
    }

    [Test]
    public void Summary_IsNotEmpty()
    {
        Assert.That(strategy.Summary, Is.Not.Empty);
    }

    [Test]
    public void DeriveKey_ReturnsCorrectKeySize()
    {
        var salt = new byte[32];
        RandomNumberGenerator.Fill(salt);

        byte[] key = strategy.DeriveKey("testPassword123!", salt, 256);

        Assert.That(key, Has.Length.EqualTo(32));
    }

    [Test]
    public void DeriveKey_SameInputs_ProducesSameKey()
    {
        var salt = new byte[32];
        RandomNumberGenerator.Fill(salt);

        byte[] key1 = strategy.DeriveKey("password", salt, 256);
        byte[] key2 = strategy.DeriveKey("password", salt, 256);

        Assert.That(key1, Is.EqualTo(key2));
    }

    [Test]
    public void DeriveKey_DifferentPasswords_ProducesDifferentKeys()
    {
        var salt = new byte[32];
        RandomNumberGenerator.Fill(salt);

        byte[] key1 = strategy.DeriveKey("password1", salt, 256);
        byte[] key2 = strategy.DeriveKey("password2", salt, 256);

        Assert.That(key1, Is.Not.EqualTo(key2));
    }

    [Test]
    public void DeriveKey_DifferentSalts_ProducesDifferentKeys()
    {
        var salt1 = new byte[32];
        var salt2 = new byte[32];
        RandomNumberGenerator.Fill(salt1);
        RandomNumberGenerator.Fill(salt2);

        byte[] key1 = strategy.DeriveKey("password", salt1, 256);
        byte[] key2 = strategy.DeriveKey("password", salt2, 256);

        Assert.That(key1, Is.Not.EqualTo(key2));
    }

    [Test]
    public void DeriveKey_ReturnsNonZeroKey()
    {
        var salt = new byte[32];
        RandomNumberGenerator.Fill(salt);

        byte[] key = strategy.DeriveKey("password", salt, 256);

        Assert.That(key.Any(b => b != 0), Is.True);
    }
}
