using CloudZCrypt.Application.Orchestrators.Interfaces;
using CloudZCrypt.Application.Services.Interfaces;
using CloudZCrypt.Application.Validators.Interfaces;
using CloudZCrypt.Composition;
using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Factories.Interfaces;
using CloudZCrypt.Domain.Services.Interfaces;
using CloudZCrypt.Domain.Strategies.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace CloudZCrypt.Test.Composition;

[TestFixture]
internal sealed class DependencyInjectionTests
{
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        ServiceCollection services = new();
        services.AddDomainServices();
        services.AddApplicationServices();
        _provider = services.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {
        _provider.Dispose();
    }

    [Test]
    public void AddDomainServices_ResolvesKeyDerivationServiceFactory()
    {
        IKeyDerivationServiceFactory factory = _provider.GetRequiredService<IKeyDerivationServiceFactory>();

        Assert.That(factory, Is.Not.Null);
    }

    [Test]
    public void AddDomainServices_ResolvesEncryptionServiceFactory()
    {
        IEncryptionServiceFactory factory = _provider.GetRequiredService<IEncryptionServiceFactory>();

        Assert.That(factory, Is.Not.Null);
    }

    [Test]
    public void AddDomainServices_ResolvesCompressionServiceFactory()
    {
        ICompressionServiceFactory factory = _provider.GetRequiredService<ICompressionServiceFactory>();

        Assert.That(factory, Is.Not.Null);
    }

    [Test]
    public void AddDomainServices_ResolvesNameObfuscationServiceFactory()
    {
        INameObfuscationServiceFactory factory = _provider.GetRequiredService<INameObfuscationServiceFactory>();

        Assert.That(factory, Is.Not.Null);
    }

    [Test]
    public void AddDomainServices_ResolvesPasswordService()
    {
        IPasswordService service = _provider.GetRequiredService<IPasswordService>();

        Assert.That(service, Is.Not.Null);
    }

    [Test]
    public void AddDomainServices_ResolvesFileOperationsService()
    {
        IFileOperationsService service = _provider.GetRequiredService<IFileOperationsService>();

        Assert.That(service, Is.Not.Null);
    }

    [Test]
    public void AddDomainServices_ResolvesSystemStorageService()
    {
        ISystemStorageService service = _provider.GetRequiredService<ISystemStorageService>();

        Assert.That(service, Is.Not.Null);
    }

    [Test]
    public void AddApplicationServices_ResolvesFileCryptOrchestrator()
    {
        IFileCryptOrchestrator orchestrator = _provider.GetRequiredService<IFileCryptOrchestrator>();

        Assert.That(orchestrator, Is.Not.Null);
    }

    [Test]
    public void AddApplicationServices_ResolvesFileCryptSingleFileService()
    {
        IFileCryptSingleFileService service = _provider.GetRequiredService<IFileCryptSingleFileService>();

        Assert.That(service, Is.Not.Null);
    }

    [Test]
    public void AddApplicationServices_ResolvesFileCryptDirectoryService()
    {
        IFileCryptDirectoryService service = _provider.GetRequiredService<IFileCryptDirectoryService>();

        Assert.That(service, Is.Not.Null);
    }

    [Test]
    public void AddApplicationServices_ResolvesFileCryptRequestValidator()
    {
        IFileCryptRequestValidator validator = _provider.GetRequiredService<IFileCryptRequestValidator>();

        Assert.That(validator, Is.Not.Null);
    }

    [Test]
    public void AddApplicationServices_ResolvesManifestService()
    {
        IManifestService service = _provider.GetRequiredService<IManifestService>();

        Assert.That(service, Is.Not.Null);
    }

    [TestCase(EncryptionAlgorithm.Aes)]
    [TestCase(EncryptionAlgorithm.Twofish)]
    [TestCase(EncryptionAlgorithm.Serpent)]
    [TestCase(EncryptionAlgorithm.ChaCha20)]
    [TestCase(EncryptionAlgorithm.Camellia)]
    public void EncryptionFactory_ResolvesAllAlgorithms(EncryptionAlgorithm algorithm)
    {
        IEncryptionServiceFactory factory = _provider.GetRequiredService<IEncryptionServiceFactory>();

        IEncryptionAlgorithmStrategy strategy = factory.Create(algorithm);

        Assert.That(strategy, Is.Not.Null);
        Assert.That(strategy.Id, Is.EqualTo(algorithm));
    }

    [TestCase(KeyDerivationAlgorithm.Argon2id)]
    [TestCase(KeyDerivationAlgorithm.PBKDF2)]
    public void KeyDerivationFactory_ResolvesAllAlgorithms(KeyDerivationAlgorithm algorithm)
    {
        IKeyDerivationServiceFactory factory = _provider.GetRequiredService<IKeyDerivationServiceFactory>();

        IKeyDerivationAlgorithmStrategy strategy = factory.Create(algorithm);

        Assert.That(strategy, Is.Not.Null);
        Assert.That(strategy.Id, Is.EqualTo(algorithm));
    }

    [TestCase(CompressionMode.None)]
    [TestCase(CompressionMode.GZip)]
    [TestCase(CompressionMode.BZip2)]
    [TestCase(CompressionMode.LZMA)]
    public void CompressionFactory_ResolvesAllModes(CompressionMode mode)
    {
        ICompressionServiceFactory factory = _provider.GetRequiredService<ICompressionServiceFactory>();

        ICompressionStrategy strategy = factory.Create(mode);

        Assert.That(strategy, Is.Not.Null);
        Assert.That(strategy.Id, Is.EqualTo(mode));
    }

    [TestCase(NameObfuscationMode.None)]
    [TestCase(NameObfuscationMode.Guid)]
    [TestCase(NameObfuscationMode.Sha256)]
    [TestCase(NameObfuscationMode.Sha512)]
    public void ObfuscationFactory_ResolvesAllModes(NameObfuscationMode mode)
    {
        INameObfuscationServiceFactory factory = _provider.GetRequiredService<INameObfuscationServiceFactory>();

        INameObfuscationStrategy strategy = factory.Create(mode);

        Assert.That(strategy, Is.Not.Null);
        Assert.That(strategy.Id, Is.EqualTo(mode));
    }
}
