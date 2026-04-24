namespace BackupZCrypt.Test.Composition;

using BackupZCrypt.Application.Orchestrators.Interfaces;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.Validators.Interfaces;
using BackupZCrypt.Composition;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Factories.Interfaces;
using BackupZCrypt.Domain.Services.Interfaces;
using BackupZCrypt.Domain.Strategies.Interfaces;
using Microsoft.Extensions.DependencyInjection;

[TestFixture]
internal sealed class DependencyInjectionTests
{
    private ServiceProvider provider = null!;

    [SetUp]
    public void SetUp()
    {
        ServiceCollection services = [];
        services.AddDomainServices();
        services.AddApplicationServices();
        this.provider = services.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {
        this.provider.Dispose();
    }

    [Test]
    public void AddDomainServices_ResolvesKeyDerivationServiceFactory()
    {
        var factory =
            this.provider.GetRequiredService<IKeyDerivationServiceFactory>();

        Assert.That(factory, Is.Not.Null);
    }

    [Test]
    public void AddDomainServices_ResolvesEncryptionServiceFactory()
    {
        var factory =
            this.provider.GetRequiredService<IEncryptionServiceFactory>();

        Assert.That(factory, Is.Not.Null);
    }

    [Test]
    public void AddDomainServices_ResolvesCompressionServiceFactory()
    {
        var factory =
            this.provider.GetRequiredService<ICompressionServiceFactory>();

        Assert.That(factory, Is.Not.Null);
    }

    [Test]
    public void AddDomainServices_ResolvesNameObfuscationServiceFactory()
    {
        var factory =
            this.provider.GetRequiredService<INameObfuscationServiceFactory>();

        Assert.That(factory, Is.Not.Null);
    }

    [Test]
    public void AddDomainServices_ResolvesPasswordService()
    {
        var service = this.provider.GetRequiredService<IPasswordService>();

        Assert.That(service, Is.Not.Null);
    }

    [Test]
    public void AddDomainServices_ResolvesFileOperationsService()
    {
        var service = this.provider.GetRequiredService<IFileOperationsService>();

        Assert.That(service, Is.Not.Null);
    }

    [Test]
    public void AddDomainServices_ResolvesSystemStorageService()
    {
        var service = this.provider.GetRequiredService<ISystemStorageService>();

        Assert.That(service, Is.Not.Null);
    }

    [Test]
    public void AddApplicationServices_ResolvesBackupOrchestrator()
    {
        var orchestrator =
            this.provider.GetRequiredService<IBackupOrchestrator>();

        Assert.That(orchestrator, Is.Not.Null);
    }

    [Test]
    public void AddApplicationServices_ResolvesSingleFileBackupService()
    {
        var service =
            this.provider.GetRequiredService<ISingleFileBackupService>();

        Assert.That(service, Is.Not.Null);
    }

    [Test]
    public void AddApplicationServices_ResolvesDirectoryBackupService()
    {
        var service =
            this.provider.GetRequiredService<IDirectoryBackupService>();

        Assert.That(service, Is.Not.Null);
    }

    [Test]
    public void AddApplicationServices_ResolvesBackupRequestValidator()
    {
        var validator =
            this.provider.GetRequiredService<IBackupRequestValidator>();

        Assert.That(validator, Is.Not.Null);
    }

    [Test]
    public void AddApplicationServices_ResolvesManifestService()
    {
        var service = this.provider.GetRequiredService<IManifestService>();

        Assert.That(service, Is.Not.Null);
    }

    [TestCase(EncryptionAlgorithm.Aes)]
    [TestCase(EncryptionAlgorithm.Twofish)]
    [TestCase(EncryptionAlgorithm.Serpent)]
    [TestCase(EncryptionAlgorithm.ChaCha20)]
    [TestCase(EncryptionAlgorithm.Camellia)]
    public void EncryptionFactory_ResolvesAllAlgorithms(EncryptionAlgorithm algorithm)
    {
        var factory =
            this.provider.GetRequiredService<IEncryptionServiceFactory>();

        var strategy = factory.Create(algorithm);

        Assert.That(strategy, Is.Not.Null);
        Assert.That(strategy.Id, Is.EqualTo(algorithm));
    }

    [TestCase(KeyDerivationAlgorithm.Argon2id)]
    [TestCase(KeyDerivationAlgorithm.PBKDF2)]
    [TestCase(KeyDerivationAlgorithm.Scrypt)]
    public void KeyDerivationFactory_ResolvesAllAlgorithms(KeyDerivationAlgorithm algorithm)
    {
        var factory =
            this.provider.GetRequiredService<IKeyDerivationServiceFactory>();

        var strategy = factory.Create(algorithm);

        Assert.That(strategy, Is.Not.Null);
        Assert.That(strategy.Id, Is.EqualTo(algorithm));
    }

    [TestCase(CompressionMode.ZstdFast)]
    [TestCase(CompressionMode.Zstd)]
    [TestCase(CompressionMode.ZstdBest)]
    public void CompressionFactory_ResolvesAllModes(CompressionMode mode)
    {
        var factory =
            this.provider.GetRequiredService<ICompressionServiceFactory>();

        var strategy = factory.Create(mode);

        Assert.That(strategy, Is.Not.Null);
        Assert.That(strategy.Id, Is.EqualTo(mode));
    }

    [TestCase(NameObfuscationMode.Guid)]
    [TestCase(NameObfuscationMode.Sha256)]
    [TestCase(NameObfuscationMode.Sha512)]
    public void ObfuscationFactory_ResolvesAllModes(NameObfuscationMode mode)
    {
        var factory =
            this.provider.GetRequiredService<INameObfuscationServiceFactory>();

        var strategy = factory.Create(mode);

        Assert.That(strategy, Is.Not.Null);
        Assert.That(strategy.Id, Is.EqualTo(mode));
    }
}
