namespace CloudZCrypt.Test.Application.Services;

using CloudZCrypt.Application.Services;
using CloudZCrypt.Application.ValueObjects;
using CloudZCrypt.Domain.Constants;
using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Exceptions;
using CloudZCrypt.Domain.Factories.Interfaces;
using CloudZCrypt.Domain.Services.Interfaces;
using CloudZCrypt.Domain.Strategies.Interfaces;
using CloudZCrypt.Domain.ValueObjects.FileCrypt;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

[TestFixture]
internal sealed class FileCryptSingleFileServiceTests
{
    private IEncryptionServiceFactory encryptionFactory = null!;
    private IEncryptionAlgorithmStrategy encryptionStrategy = null!;
    private IFileOperationsService fileOps = null!;
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
        this.encryptionStrategy
            .DecryptFileAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<KeyDerivationAlgorithm>())
            .Returns(true);

        Result<FileCryptResult> result = await service.ProcessAsync(
            @"C:\source\file.czc",
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
        this.fileOps.CombinePath(Arg.Any<string[]>()).Returns(@"C:\dest\obfuscated.czc");
        this.obfuscationStrategy
            .ObfuscateFileName(Arg.Any<string>(), Arg.Any<string>())
            .Returns("obfuscated.czc");
        this.encryptionStrategy
            .EncryptFileAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<KeyDerivationAlgorithm>(),
                Arg.Any<CompressionMode>())
            .Returns(true);

        await service.ProcessAsync(
            @"C:\source\file.txt",
            @"C:\dest\file.czc",
            CreateRequest(),
            progress,
            CancellationToken.None);

        this.obfuscationStrategy.Received(1).ObfuscateFileName(Arg.Any<string>(), Arg.Any<string>());
    }

    [Test]
    public async Task ProcessAsync_Encrypt_SuccessfulEncryption_ReturnsSuccess()
    {
        this.fileOps.GetFileSize(Arg.Any<string>()).Returns(100L);
        this.fileOps.GetDirectoryName(Arg.Any<string>()).Returns(@"C:\dest");
        this.fileOps.CombinePath(Arg.Any<string[]>()).Returns(@"C:\dest\file.czc");
        this.obfuscationStrategy
            .ObfuscateFileName(Arg.Any<string>(), Arg.Any<string>())
            .Returns("file.czc");
        this.encryptionStrategy
            .EncryptFileAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<KeyDerivationAlgorithm>(),
                Arg.Any<CompressionMode>())
            .Returns(true);

        Result<FileCryptResult> result = await service.ProcessAsync(
            @"C:\source\file.txt",
            @"C:\dest\file.czc",
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
        this.fileOps.CombinePath(Arg.Any<string[]>()).Returns(@"C:\dest\file.czc");
        this.obfuscationStrategy
            .ObfuscateFileName(Arg.Any<string>(), Arg.Any<string>())
            .Returns("file.czc");
        this.encryptionStrategy
            .EncryptFileAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<KeyDerivationAlgorithm>(),
                Arg.Any<CompressionMode>())
            .ThrowsAsync(new EncryptionInvalidPasswordException());

        Result<FileCryptResult> result = await service.ProcessAsync(
            @"C:\source\file.txt",
            @"C:\dest\file.czc",
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
        this.fileOps.CombinePath(Arg.Any<string[]>()).Returns(@"C:\dest\file.czc");
        this.obfuscationStrategy
            .ObfuscateFileName(Arg.Any<string>(), Arg.Any<string>())
            .Returns("file.czc");
        this.encryptionStrategy
            .EncryptFileAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<KeyDerivationAlgorithm>(),
                Arg.Any<CompressionMode>())
            .Returns(true);

        await service.ProcessAsync(
            @"C:\source\file.txt",
            @"C:\dest\file.czc",
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

        this.encryptionStrategy = Substitute.For<IEncryptionAlgorithmStrategy>();
        this.obfuscationStrategy = Substitute.For<INameObfuscationStrategy>();

        this.encryptionFactory.Create(Arg.Any<EncryptionAlgorithm>()).Returns(this.encryptionStrategy);
        this.obfuscationFactory.Create(Arg.Any<NameObfuscationMode>()).Returns(this.obfuscationStrategy);

        this.progress = Substitute.For<IProgress<FileCryptStatus>>();

        this.service = new FileCryptSingleFileService(
            this.encryptionFactory,
            this.obfuscationFactory,
            this.fileOps);
    }

    private static FileCryptRequest CreateRequest(
        EncryptOperation operation = EncryptOperation.Encrypt) =>
        new(
            @"C:\source\file.txt",
            @"C:\dest\file.czc",
            "StrongP@ss1",
            "StrongP@ss1",
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.Argon2id,
            operation,
            NameObfuscationMode.None);
}
