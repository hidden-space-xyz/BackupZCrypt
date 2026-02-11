using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Infrastructure.Strategies.KeyDerivation;

namespace CloudZCrypt.Test.Infrastructure.Strategies.KeyDerivation;

[TestFixture]
internal sealed class Argon2IdKeyDerivationStrategyTests
{
    private Argon2IdKeyDerivationStrategy _strategy = null!;

    [SetUp]
    public void SetUp()
    {
        _strategy = new Argon2IdKeyDerivationStrategy();
    }

    [Test]
    public void Id_ReturnsArgon2id()
    {
        Assert.That(_strategy.Id, Is.EqualTo(KeyDerivationAlgorithm.Argon2id));
    }

    [Test]
    public void DisplayName_ReturnsArgon2id()
    {
        Assert.That(_strategy.DisplayName, Is.EqualTo("Argon2id"));
    }

    [Test]
    public void Description_IsNotEmpty()
    {
        Assert.That(_strategy.Description, Is.Not.Empty);
    }

    [Test]
    public void Summary_IsNotEmpty()
    {
        Assert.That(_strategy.Summary, Is.Not.Empty);
    }

    [Test]
    public void DeriveKey_ReturnsCorrectKeySize()
    {
        byte[] salt = new byte[32];
        Random.Shared.NextBytes(salt);

        byte[] key = _strategy.DeriveKey("testPassword123!", salt, 256);

        Assert.That(key, Has.Length.EqualTo(32));
    }

    [Test]
    public void DeriveKey_SameInputs_ProducesSameKey()
    {
        byte[] salt = new byte[32];
        Random.Shared.NextBytes(salt);

        byte[] key1 = _strategy.DeriveKey("password", salt, 256);
        byte[] key2 = _strategy.DeriveKey("password", salt, 256);

        Assert.That(key1, Is.EqualTo(key2));
    }

    [Test]
    public void DeriveKey_DifferentPasswords_ProducesDifferentKeys()
    {
        byte[] salt = new byte[32];
        Random.Shared.NextBytes(salt);

        byte[] key1 = _strategy.DeriveKey("password1", salt, 256);
        byte[] key2 = _strategy.DeriveKey("password2", salt, 256);

        Assert.That(key1, Is.Not.EqualTo(key2));
    }

    [Test]
    public void DeriveKey_DifferentSalts_ProducesDifferentKeys()
    {
        byte[] salt1 = new byte[32];
        byte[] salt2 = new byte[32];
        Random.Shared.NextBytes(salt1);
        Random.Shared.NextBytes(salt2);

        byte[] key1 = _strategy.DeriveKey("password", salt1, 256);
        byte[] key2 = _strategy.DeriveKey("password", salt2, 256);

        Assert.That(key1, Is.Not.EqualTo(key2));
    }

    [Test]
    public void DeriveKey_ReturnsNonZeroKey()
    {
        byte[] salt = new byte[32];
        Random.Shared.NextBytes(salt);

        byte[] key = _strategy.DeriveKey("password", salt, 256);

        Assert.That(key.Any(b => b != 0), Is.True);
    }
}
