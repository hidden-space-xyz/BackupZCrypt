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
        ServiceCollection services = new();
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
        IKeyDerivationServiceFactory factory =
            this.provider.GetRequiredService<IKeyDerivationServiceFactory>();

        Assert.That(factory, Is.Not.Null);
    }

    [Test]
    public void AddDomainServices_ResolvesEncryptionServiceFactory()
    {
        IEncryptionServiceFactory factory =
            this.provider.GetRequiredService<IEncryptionServiceFactory>();

        Assert.That(factory, Is.Not.Null);
    }

    [Test]
    public void AddDomainServices_ResolvesCompressionServiceFactory()
    {
        ICompressionServiceFactory factory =
            this.provider.GetRequiredService<ICompressionServiceFactory>();

        Assert.That(factory, Is.Not.Null);
    }

    [Test]
    public void AddDomainServices_ResolvesNameObfuscationServiceFactory()
    {
        INameObfuscationServiceFactory factory =
            this.provider.GetRequiredService<INameObfuscationServiceFactory>();

        Assert.That(factory, Is.Not.Null);
    }

    [Test]
    public void AddDomainServices_ResolvesPasswordService()
    {
        IPasswordService service = this.provider.GetRequiredService<IPasswordService>();

        Assert.That(service, Is.Not.Null);
    }

    [Test]
    public void AddDomainServices_ResolvesFileOperationsService()
    {
        IFileOperationsService service = this.provider.GetRequiredService<IFileOperationsService>();

        Assert.That(service, Is.Not.Null);
    }

    [Test]
    public void AddDomainServices_ResolvesSystemStorageService()
    {
        ISystemStorageService service = this.provider.GetRequiredService<ISystemStorageService>();

        Assert.That(service, Is.Not.Null);
    }

    [Test]
    public void AddApplicationServices_ResolvesFileCryptOrchestrator()
    {
        IFileCryptOrchestrator orchestrator =
            this.provider.GetRequiredService<IFileCryptOrchestrator>();

        Assert.That(orchestrator, Is.Not.Null);
    }

    [Test]
    public void AddApplicationServices_ResolvesFileCryptSingleFileService()
    {
        IFileCryptSingleFileService service =
            this.provider.GetRequiredService<IFileCryptSingleFileService>();

        Assert.That(service, Is.Not.Null);
    }

    [Test]
    public void AddApplicationServices_ResolvesFileCryptDirectoryService()
    {
        IFileCryptDirectoryService service =
            this.provider.GetRequiredService<IFileCryptDirectoryService>();

        Assert.That(service, Is.Not.Null);
    }

    [Test]
    public void AddApplicationServices_ResolvesFileCryptRequestValidator()
    {
        IFileCryptRequestValidator validator =
            this.provider.GetRequiredService<IFileCryptRequestValidator>();

        Assert.That(validator, Is.Not.Null);
    }

    [Test]
    public void AddApplicationServices_ResolvesManifestService()
    {
        IManifestService service = this.provider.GetRequiredService<IManifestService>();

        Assert.That(service, Is.Not.Null);
    }

    [TestCase(EncryptionAlgorithm.Aes)]
    [TestCase(EncryptionAlgorithm.Twofish)]
    [TestCase(EncryptionAlgorithm.Serpent)]
    [TestCase(EncryptionAlgorithm.ChaCha20)]
    [TestCase(EncryptionAlgorithm.Camellia)]
    public void EncryptionFactory_ResolvesAllAlgorithms(EncryptionAlgorithm algorithm)
    {
        IEncryptionServiceFactory factory =
            this.provider.GetRequiredService<IEncryptionServiceFactory>();

        IEncryptionAlgorithmStrategy strategy = factory.Create(algorithm);

        Assert.That(strategy, Is.Not.Null);
        Assert.That(strategy.Id, Is.EqualTo(algorithm));
    }

    [TestCase(KeyDerivationAlgorithm.Argon2id)]
    [TestCase(KeyDerivationAlgorithm.PBKDF2)]
    [TestCase(KeyDerivationAlgorithm.Scrypt)]
    public void KeyDerivationFactory_ResolvesAllAlgorithms(KeyDerivationAlgorithm algorithm)
    {
        IKeyDerivationServiceFactory factory =
            this.provider.GetRequiredService<IKeyDerivationServiceFactory>();

        IKeyDerivationAlgorithmStrategy strategy = factory.Create(algorithm);

        Assert.That(strategy, Is.Not.Null);
        Assert.That(strategy.Id, Is.EqualTo(algorithm));
    }

    [TestCase(CompressionMode.ZstdFast)]
    [TestCase(CompressionMode.Zstd)]
    [TestCase(CompressionMode.ZstdBest)]
    public void CompressionFactory_ResolvesAllModes(CompressionMode mode)
    {
        ICompressionServiceFactory factory =
            this.provider.GetRequiredService<ICompressionServiceFactory>();

        ICompressionStrategy strategy = factory.Create(mode);

        Assert.That(strategy, Is.Not.Null);
        Assert.That(strategy.Id, Is.EqualTo(mode));
    }

    [TestCase(NameObfuscationMode.Guid)]
    [TestCase(NameObfuscationMode.Sha256)]
    [TestCase(NameObfuscationMode.Sha512)]
    public void ObfuscationFactory_ResolvesAllModes(NameObfuscationMode mode)
    {
        INameObfuscationServiceFactory factory =
            this.provider.GetRequiredService<INameObfuscationServiceFactory>();

        INameObfuscationStrategy strategy = factory.Create(mode);

        Assert.That(strategy, Is.Not.Null);
        Assert.That(strategy.Id, Is.EqualTo(mode));
    }
}
