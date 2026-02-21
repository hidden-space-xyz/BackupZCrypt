namespace CloudZCrypt.Test.Infrastructure.Strategies.KeyDerivation;

using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Infrastructure.Strategies.KeyDerivation;
using System.Security.Cryptography;

[TestFixture]
internal sealed class Pbkdf2KeyDerivationStrategyTests
{
    private Pbkdf2KeyDerivationStrategy strategy = null!;

    [SetUp]
    public void SetUp()
    {
        this.strategy = new Pbkdf2KeyDerivationStrategy();
    }

    [Test]
    public void Id_ReturnsPBKDF2()
    {
        Assert.That(this.strategy.Id, Is.EqualTo(KeyDerivationAlgorithm.PBKDF2));
    }

    [Test]
    public void DisplayName_ContainsPBKDF2()
    {
        Assert.That(this.strategy.DisplayName, Does.Contain("PBKDF2"));
    }

    [Test]
    public void Description_IsNotEmpty()
    {
        Assert.That(this.strategy.Description, Is.Not.Empty);
    }

    [Test]
    public void Summary_IsNotEmpty()
    {
        Assert.That(this.strategy.Summary, Is.Not.Empty);
    }

    [Test]
    public void DeriveKey_ReturnsCorrectKeySize()
    {
        byte[] salt = new byte[32];
        RandomNumberGenerator.Fill(salt);

        byte[] key = this.strategy.DeriveKey("testPassword123!", salt, 256);

        Assert.That(key, Has.Length.EqualTo(32));
    }

    [Test]
    public void DeriveKey_SameInputs_ProducesSameKey()
    {
        byte[] salt = new byte[32];
        RandomNumberGenerator.Fill(salt);

        byte[] key1 = this.strategy.DeriveKey("password", salt, 256);
        byte[] key2 = this.strategy.DeriveKey("password", salt, 256);

        Assert.That(key1, Is.EqualTo(key2));
    }

    [Test]
    public void DeriveKey_DifferentPasswords_ProducesDifferentKeys()
    {
        byte[] salt = new byte[32];
        RandomNumberGenerator.Fill(salt);

        byte[] key1 = this.strategy.DeriveKey("password1", salt, 256);
        byte[] key2 = this.strategy.DeriveKey("password2", salt, 256);

        Assert.That(key1, Is.Not.EqualTo(key2));
    }

    [Test]
    public void DeriveKey_DifferentSalts_ProducesDifferentKeys()
    {
        byte[] salt1 = new byte[32];
        byte[] salt2 = new byte[32];
        RandomNumberGenerator.Fill(salt1);
        RandomNumberGenerator.Fill(salt2);

        byte[] key1 = this.strategy.DeriveKey("password", salt1, 256);
        byte[] key2 = this.strategy.DeriveKey("password", salt2, 256);

        Assert.That(key1, Is.Not.EqualTo(key2));
    }

    [Test]
    public void DeriveKey_ReturnsNonZeroKey()
    {
        byte[] salt = new byte[32];
        RandomNumberGenerator.Fill(salt);

        byte[] key = this.strategy.DeriveKey("password", salt, 256);

        Assert.That(key.Any(b => b != 0), Is.True);
    }
}
