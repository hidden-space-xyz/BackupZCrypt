using CloudZCrypt.Composition;
using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Strategies.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace CloudZCrypt.Test.Integration;

[TestFixture]
internal sealed class KeyDerivationRoundTripTests
{
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        ServiceCollection services = new();
        services.AddDomainServices();
        _provider = services.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {
        _provider.Dispose();
    }

    [TestCase(KeyDerivationAlgorithm.Argon2id)]
    [TestCase(KeyDerivationAlgorithm.PBKDF2)]
    public void AllStrategies_DeriveKey_ProducesCorrectSize(KeyDerivationAlgorithm algorithm)
    {
        IEnumerable<IKeyDerivationAlgorithmStrategy> strategies = _provider.GetRequiredService<
            IEnumerable<IKeyDerivationAlgorithmStrategy>
        >();

        IKeyDerivationAlgorithmStrategy strategy = strategies.First(s => s.Id == algorithm);
        byte[] salt = new byte[32];
        Random.Shared.NextBytes(salt);

        byte[] key = strategy.DeriveKey("password123!", salt, 256);

        Assert.That(key, Has.Length.EqualTo(32));
    }

    [TestCase(KeyDerivationAlgorithm.Argon2id)]
    [TestCase(KeyDerivationAlgorithm.PBKDF2)]
    public void AllStrategies_SameInputsSameOutput(KeyDerivationAlgorithm algorithm)
    {
        IEnumerable<IKeyDerivationAlgorithmStrategy> strategies = _provider.GetRequiredService<
            IEnumerable<IKeyDerivationAlgorithmStrategy>
        >();

        IKeyDerivationAlgorithmStrategy strategy = strategies.First(s => s.Id == algorithm);
        byte[] salt = new byte[32];
        Random.Shared.NextBytes(salt);

        byte[] key1 = strategy.DeriveKey("password", salt, 256);
        byte[] key2 = strategy.DeriveKey("password", salt, 256);

        Assert.That(key1, Is.EqualTo(key2));
    }

    [TestCase(KeyDerivationAlgorithm.Argon2id)]
    [TestCase(KeyDerivationAlgorithm.PBKDF2)]
    public void AllStrategies_DifferentPasswordsDifferentKeys(KeyDerivationAlgorithm algorithm)
    {
        IEnumerable<IKeyDerivationAlgorithmStrategy> strategies = _provider.GetRequiredService<
            IEnumerable<IKeyDerivationAlgorithmStrategy>
        >();

        IKeyDerivationAlgorithmStrategy strategy = strategies.First(s => s.Id == algorithm);
        byte[] salt = new byte[32];
        Random.Shared.NextBytes(salt);

        byte[] key1 = strategy.DeriveKey("password1", salt, 256);
        byte[] key2 = strategy.DeriveKey("password2", salt, 256);

        Assert.That(key1, Is.Not.EqualTo(key2));
    }

    [TestCase(KeyDerivationAlgorithm.Argon2id)]
    [TestCase(KeyDerivationAlgorithm.PBKDF2)]
    public void AllStrategies_HaveMetadata(KeyDerivationAlgorithm algorithm)
    {
        IEnumerable<IKeyDerivationAlgorithmStrategy> strategies = _provider.GetRequiredService<
            IEnumerable<IKeyDerivationAlgorithmStrategy>
        >();

        IKeyDerivationAlgorithmStrategy strategy = strategies.First(s => s.Id == algorithm);

        Assert.That(strategy.DisplayName, Is.Not.Null.And.Not.Empty);
        Assert.That(strategy.Description, Is.Not.Null.And.Not.Empty);
        Assert.That(strategy.Summary, Is.Not.Null.And.Not.Empty);
    }
}
