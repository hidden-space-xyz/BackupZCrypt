namespace BackupZCrypt.Test.Infrastructure.Strategies.Encryption;

using BackupZCrypt.Composition;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;
using Microsoft.Extensions.DependencyInjection;

[TestFixture]
internal sealed class EncryptionStrategyMetadataTests
{
    private ServiceProvider provider = null!;

    [SetUp]
    public void SetUp()
    {
        ServiceCollection services = [];
        services.AddDomainServices();
        this.provider = services.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {
        this.provider.Dispose();
    }

    [TestCase(EncryptionAlgorithm.Aes, "AES-256 GCM")]
    [TestCase(EncryptionAlgorithm.Twofish, "Twofish-256 GCM")]
    [TestCase(EncryptionAlgorithm.Serpent, "Serpent-256 GCM")]
    [TestCase(EncryptionAlgorithm.ChaCha20, "ChaCha20-Poly1305")]
    [TestCase(EncryptionAlgorithm.Camellia, "Camellia-256 GCM")]
    public void DisplayName_ReturnsExpected(EncryptionAlgorithm algorithm, string expectedName)
    {
        var strategies = this.provider.GetRequiredService<
            IEnumerable<IEncryptionAlgorithmStrategy>
        >();

        var strategy = strategies.First(s => s.Id == algorithm);

        Assert.That(strategy.DisplayName, Is.EqualTo(expectedName));
    }

    [TestCase(EncryptionAlgorithm.Aes)]
    [TestCase(EncryptionAlgorithm.Twofish)]
    [TestCase(EncryptionAlgorithm.Serpent)]
    [TestCase(EncryptionAlgorithm.ChaCha20)]
    [TestCase(EncryptionAlgorithm.Camellia)]
    public void Description_IsNotEmpty(EncryptionAlgorithm algorithm)
    {
        var strategies = this.provider.GetRequiredService<
            IEnumerable<IEncryptionAlgorithmStrategy>
        >();

        var strategy = strategies.First(s => s.Id == algorithm);

        Assert.That(strategy.Description, Is.Not.Null.And.Not.Empty);
    }

    [TestCase(EncryptionAlgorithm.Aes)]
    [TestCase(EncryptionAlgorithm.Twofish)]
    [TestCase(EncryptionAlgorithm.Serpent)]
    [TestCase(EncryptionAlgorithm.ChaCha20)]
    [TestCase(EncryptionAlgorithm.Camellia)]
    public void Summary_IsNotEmpty(EncryptionAlgorithm algorithm)
    {
        var strategies = this.provider.GetRequiredService<
            IEnumerable<IEncryptionAlgorithmStrategy>
        >();

        var strategy = strategies.First(s => s.Id == algorithm);

        Assert.That(strategy.Summary, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void AllAlgorithms_HaveUniqueIds()
    {
        var strategies = this.provider.GetRequiredService<
            IEnumerable<IEncryptionAlgorithmStrategy>
        >();

        EncryptionAlgorithm[] ids = [.. strategies.Select(s => s.Id)];

        Assert.That(ids, Is.Unique);
    }

    [Test]
    public void AllAlgorithms_AreRegistered()
    {
        var strategies = this.provider.GetRequiredService<
            IEnumerable<IEncryptionAlgorithmStrategy>
        >();

        Assert.That(strategies.Count(), Is.EqualTo(5));
    }
}
