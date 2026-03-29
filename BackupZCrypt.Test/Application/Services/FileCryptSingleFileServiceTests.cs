namespace BackupZCrypt.Test.Application.Services;

using BackupZCrypt.Application.Services;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Application.ValueObjects.Manifest;
using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Exceptions;
using BackupZCrypt.Domain.Factories.Interfaces;
using BackupZCrypt.Domain.Services.Interfaces;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Encryption;
using BackupZCrypt.Domain.ValueObjects.FileCrypt;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

[TestFixture]
internal sealed class FileCryptSingleFileServiceTests
{
    private IEncryptionServiceFactory encryptionFactory = null!;
    private IEncryptionAlgorithmStrategy encryptionStrategy = null!;
    private IFileOperationsService fileOps = null!;
    private IManifestService manifestService = null!;
    private INameObfuscationServiceFactory obfuscationFactory = null!;
    private INameObfuscationStrategy obfuscationStrategy = null!;
    private IProgress<FileCryptStatus> progress = null!;
    private FileCryptSingleFileService service = null!;

    [Test]
    public async Task ProcessAsync_Decrypt_ManifestFile_IgnoresAndReturnsSuccess()
    {
        string manifestPath = @$"C:\source\{FileCryptConstants.ManifestFileName}";

        Result<FileCryptResult> result = await service.ProcessAsync(
            manifestPath,
            @"C:\dest\manifest",
            CreateRequest(EncryptOperation.Decrypt),
            progress,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.ProcessedFiles, Is.EqualTo(0));
        }
    }

    [Test]
    public async Task ProcessAsync_Decrypt_SuccessfulDecryption_ReturnsSuccess()
    {
        this.fileOps.GetFileSize(Arg.Any<string>()).Returns(200L);

        ManifestData manifestData = new(
            new ManifestHeader(
                EncryptionAlgorithm.Aes,
                KeyDerivationAlgorithm.Argon2id,
                NameObfuscationMode.None,
                CompressionMode.None),
            new Dictionary<string, ManifestFileInfo>(StringComparer.OrdinalIgnoreCase)
            {
                ["file.bzc"] = new ManifestFileInfo("file.txt", new byte[16], new byte[12]),
            });

        this.manifestService
            .TryReadManifestAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<IEncryptionAlgorithmStrategy>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(manifestData);

        this.encryptionStrategy
            .DecryptFileAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<KeyDerivationAlgorithm>(),
                Arg.Any<EncryptionMetadata>(),
                Arg.Any<CancellationToken>())
            .Returns(true);

        Result<FileCryptResult> result = await service.ProcessAsync(
            @"C:\source\file.bzc",
            @"C:\dest\file.txt",
            CreateRequest(EncryptOperation.Decrypt),
            progress,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.IsSuccess, Is.True);
        }
    }

    [Test]
    public async Task ProcessAsync_Encrypt_AppliesObfuscation()
    {
        this.fileOps.GetFileSize(Arg.Any<string>()).Returns(100L);
        this.fileOps.GetDirectoryName(Arg.Any<string>()).Returns(@"C:\dest");
        this.fileOps.CombinePath(Arg.Any<string[]>()).Returns(@"C:\dest\obfuscated.bzc");
        this.obfuscationStrategy
            .ObfuscateFileName(Arg.Any<string>(), Arg.Any<string>())
            .Returns("obfuscated.bzc");
        this.encryptionStrategy
            .EncryptFileAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<KeyDerivationAlgorithm>(),
                Arg.Any<CompressionMode>(),
                Arg.Any<CancellationToken>())
            .Returns(new EncryptionMetadata(new byte[32], new byte[12], CompressionMode.None));

        await service.ProcessAsync(
            @"C:\source\file.txt",
            @"C:\dest\file.bzc",
            CreateRequest(NameObfuscation: NameObfuscationMode.Guid),
            progress,
            CancellationToken.None);

        this.obfuscationStrategy.Received(1).ObfuscateFileName(Arg.Any<string>(), Arg.Any<string>());
    }

    [Test]
    public async Task ProcessAsync_Encrypt_SuccessfulEncryption_ReturnsSuccess()
    {
        this.fileOps.GetFileSize(Arg.Any<string>()).Returns(100L);
        this.fileOps.GetDirectoryName(Arg.Any<string>()).Returns(@"C:\dest");
        this.fileOps.CombinePath(Arg.Any<string[]>()).Returns(@"C:\dest\file.bzc");
        this.obfuscationStrategy
            .ObfuscateFileName(Arg.Any<string>(), Arg.Any<string>())
            .Returns("file.bzc");
        this.encryptionStrategy
            .EncryptFileAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<KeyDerivationAlgorithm>(),
                Arg.Any<CompressionMode>(),
                Arg.Any<CancellationToken>())
            .Returns(new EncryptionMetadata(new byte[32], new byte[12], CompressionMode.None));

        Result<FileCryptResult> result = await service.ProcessAsync(
            @"C:\source\file.txt",
            @"C:\dest\file.bzc",
            CreateRequest(),
            progress,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.IsSuccess, Is.True);
            Assert.That(result.Value.ProcessedFiles, Is.EqualTo(1));
            Assert.That(result.Value.TotalFiles, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task ProcessAsync_EncryptionException_ReturnsFailure()
    {
        this.fileOps.GetFileSize(Arg.Any<string>()).Returns(100L);
        this.fileOps.GetDirectoryName(Arg.Any<string>()).Returns(@"C:\dest");
        this.fileOps.CombinePath(Arg.Any<string[]>()).Returns(@"C:\dest\file.bzc");
        this.obfuscationStrategy
            .ObfuscateFileName(Arg.Any<string>(), Arg.Any<string>())
            .Returns("file.bzc");
        this.encryptionStrategy
            .EncryptFileAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<KeyDerivationAlgorithm>(),
                Arg.Any<CompressionMode>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new EncryptionInvalidPasswordException());

        Result<FileCryptResult> result = await service.ProcessAsync(
            @"C:\source\file.txt",
            @"C:\dest\file.bzc",
            CreateRequest(),
            progress,
            CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
    }

    [Test]
    public async Task ProcessAsync_ReportsProgress()
    {
        this.fileOps.GetFileSize(Arg.Any<string>()).Returns(100L);
        this.fileOps.GetDirectoryName(Arg.Any<string>()).Returns(@"C:\dest");
        this.fileOps.CombinePath(Arg.Any<string[]>()).Returns(@"C:\dest\file.bzc");
        this.obfuscationStrategy
            .ObfuscateFileName(Arg.Any<string>(), Arg.Any<string>())
            .Returns("file.bzc");
        this.encryptionStrategy
            .EncryptFileAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<KeyDerivationAlgorithm>(),
                Arg.Any<CompressionMode>(),
                Arg.Any<CancellationToken>())
            .Returns(new EncryptionMetadata(new byte[32], new byte[12], CompressionMode.None));

        await service.ProcessAsync(
            @"C:\source\file.txt",
            @"C:\dest\file.bzc",
            CreateRequest(),
            progress,
            CancellationToken.None);

        this.progress.Received(2).Report(Arg.Any<FileCryptStatus>());
    }

    [SetUp]
    public void SetUp()
    {
        this.encryptionFactory = Substitute.For<IEncryptionServiceFactory>();
        this.obfuscationFactory = Substitute.For<INameObfuscationServiceFactory>();
        this.fileOps = Substitute.For<IFileOperationsService>();
        this.manifestService = Substitute.For<IManifestService>();

        this.encryptionStrategy = Substitute.For<IEncryptionAlgorithmStrategy>();
        this.obfuscationStrategy = Substitute.For<INameObfuscationStrategy>();

        this.encryptionFactory.Create(Arg.Any<EncryptionAlgorithm>()).Returns(this.encryptionStrategy);
        this.obfuscationFactory.Create(Arg.Any<NameObfuscationMode>()).Returns(this.obfuscationStrategy);

        this.progress = Substitute.For<IProgress<FileCryptStatus>>();

        ICompressionServiceFactory compressionFactory = Substitute.For<ICompressionServiceFactory>();

        this.service = new FileCryptSingleFileService(
            this.encryptionFactory,
            compressionFactory,
            this.obfuscationFactory,
            this.fileOps,
            this.manifestService,
            [this.encryptionStrategy]);
    }

    private static FileCryptRequest CreateRequest(
        EncryptOperation operation = EncryptOperation.Encrypt,
        NameObfuscationMode NameObfuscation = NameObfuscationMode.None) =>
        new(
            @"C:\source\file.txt",
            @"C:\dest\file.bzc",
            "StrongP@ss1",
            "StrongP@ss1",
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.Argon2id,
            operation,
            NameObfuscation);
}
