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

    [TestCase(typeof(IKeyDerivationServiceFactory))]
    [TestCase(typeof(ICompressionServiceFactory))]
    [TestCase(typeof(IPasswordService))]
    [TestCase(typeof(IFileOperationsService))]
    [TestCase(typeof(ISystemStorageService))]
    [TestCase(typeof(IBackupOrchestrator))]
    [TestCase(typeof(IFileBackupService))]
    [TestCase(typeof(IDirectoryBackupService))]
    [TestCase(typeof(IBackupRequestValidator))]
    [TestCase(typeof(IManifestService))]
    public void AllServices_Resolve(Type serviceType)
    {
        var service = this.provider.GetRequiredService(serviceType);

        Assert.That(service, Is.Not.Null);
    }

    [TestCase(KeyDerivationAlgorithm.Argon2id)]
    [TestCase(KeyDerivationAlgorithm.PBKDF2)]
    [TestCase(KeyDerivationAlgorithm.Scrypt)]
    public void KeyDerivationFactory_ResolvesAllAlgorithms(KeyDerivationAlgorithm algorithm)
    {
        var factory =
            this.provider.GetRequiredService<IKeyDerivationServiceFactory>();

        var strategy = factory.Create(algorithm);

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

        Assert.That(strategy.Id, Is.EqualTo(mode));
    }

    [Test]
    public void AllEncryptionStrategies_HaveUniqueIds()
    {
        var strategies = this.provider.GetRequiredService<
            IEnumerable<IEncryptionAlgorithmStrategy>>();

        EncryptionAlgorithm[] ids = [.. strategies.Select(s => s.Id)];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Is.Unique);
            Assert.That(ids, Has.Length.EqualTo(5));
        }
    }
}
