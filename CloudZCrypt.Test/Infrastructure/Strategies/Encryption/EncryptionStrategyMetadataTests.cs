using CloudZCrypt.Composition;
using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Strategies.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace CloudZCrypt.Test.Infrastructure.Strategies.Encryption;

[TestFixture]
internal sealed class EncryptionStrategyMetadataTests
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

    [TestCase(EncryptionAlgorithm.Aes, "AES-256 GCM")]
    [TestCase(EncryptionAlgorithm.Twofish, "Twofish-256 GCM")]
    [TestCase(EncryptionAlgorithm.Serpent, "Serpent-256 GCM")]
    [TestCase(EncryptionAlgorithm.ChaCha20, "ChaCha20-Poly1305")]
    [TestCase(EncryptionAlgorithm.Camellia, "Camellia-256 GCM")]
    public void DisplayName_ReturnsExpected(EncryptionAlgorithm algorithm, string expectedName)
    {
        IEnumerable<IEncryptionAlgorithmStrategy> strategies =
            _provider.GetRequiredService<IEnumerable<IEncryptionAlgorithmStrategy>>();

        IEncryptionAlgorithmStrategy strategy = strategies.First(s => s.Id == algorithm);

        Assert.That(strategy.DisplayName, Is.EqualTo(expectedName));
    }

    [TestCase(EncryptionAlgorithm.Aes)]
    [TestCase(EncryptionAlgorithm.Twofish)]
    [TestCase(EncryptionAlgorithm.Serpent)]
    [TestCase(EncryptionAlgorithm.ChaCha20)]
    [TestCase(EncryptionAlgorithm.Camellia)]
    public void Description_IsNotEmpty(EncryptionAlgorithm algorithm)
    {
        IEnumerable<IEncryptionAlgorithmStrategy> strategies =
            _provider.GetRequiredService<IEnumerable<IEncryptionAlgorithmStrategy>>();

        IEncryptionAlgorithmStrategy strategy = strategies.First(s => s.Id == algorithm);

        Assert.That(strategy.Description, Is.Not.Null.And.Not.Empty);
    }

    [TestCase(EncryptionAlgorithm.Aes)]
    [TestCase(EncryptionAlgorithm.Twofish)]
    [TestCase(EncryptionAlgorithm.Serpent)]
    [TestCase(EncryptionAlgorithm.ChaCha20)]
    [TestCase(EncryptionAlgorithm.Camellia)]
    public void Summary_IsNotEmpty(EncryptionAlgorithm algorithm)
    {
        IEnumerable<IEncryptionAlgorithmStrategy> strategies =
            _provider.GetRequiredService<IEnumerable<IEncryptionAlgorithmStrategy>>();

        IEncryptionAlgorithmStrategy strategy = strategies.First(s => s.Id == algorithm);

        Assert.That(strategy.Summary, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void AllAlgorithms_HaveUniqueIds()
    {
        IEnumerable<IEncryptionAlgorithmStrategy> strategies =
            _provider.GetRequiredService<IEnumerable<IEncryptionAlgorithmStrategy>>();

        EncryptionAlgorithm[] ids = strategies.Select(s => s.Id).ToArray();

        Assert.That(ids, Is.Unique);
    }

    [Test]
    public void AllAlgorithms_AreRegistered()
    {
        IEnumerable<IEncryptionAlgorithmStrategy> strategies =
            _provider.GetRequiredService<IEnumerable<IEncryptionAlgorithmStrategy>>();

        Assert.That(strategies.Count(), Is.EqualTo(5));
    }
}
