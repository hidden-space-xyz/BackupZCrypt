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

namespace CloudZCrypt.Test.Application.Services;

[TestFixture]
internal sealed class FileCryptSingleFileServiceTests
{
    private IEncryptionServiceFactory _encryptionFactory = null!;
    private INameObfuscationServiceFactory _obfuscationFactory = null!;
    private IFileOperationsService _fileOps = null!;
    private IEncryptionAlgorithmStrategy _encryptionStrategy = null!;
    private INameObfuscationStrategy _obfuscationStrategy = null!;
    private IProgress<FileCryptStatus> _progress = null!;
    private FileCryptSingleFileService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _encryptionFactory = Substitute.For<IEncryptionServiceFactory>();
        _obfuscationFactory = Substitute.For<INameObfuscationServiceFactory>();
        _fileOps = Substitute.For<IFileOperationsService>();

        _encryptionStrategy = Substitute.For<IEncryptionAlgorithmStrategy>();
        _obfuscationStrategy = Substitute.For<INameObfuscationStrategy>();

        _encryptionFactory.Create(Arg.Any<EncryptionAlgorithm>()).Returns(_encryptionStrategy);
        _obfuscationFactory.Create(Arg.Any<NameObfuscationMode>()).Returns(_obfuscationStrategy);

        _progress = Substitute.For<IProgress<FileCryptStatus>>();

        _service = new FileCryptSingleFileService(
            _encryptionFactory,
            _obfuscationFactory,
            _fileOps
        );
    }

    private static FileCryptRequest CreateRequest(
        EncryptOperation operation = EncryptOperation.Encrypt
    ) =>
        new(
            @"C:\source\file.txt",
            @"C:\dest\file.czc",
            "StrongP@ss1",
            "StrongP@ss1",
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.Argon2id,
            operation,
            NameObfuscationMode.None
        );

    [Test]
    public async Task ProcessAsync_Encrypt_SuccessfulEncryption_ReturnsSuccess()
    {
        _fileOps.GetFileSize(Arg.Any<string>()).Returns(100L);
        _fileOps.GetDirectoryName(Arg.Any<string>()).Returns(@"C:\dest");
        _fileOps.CombinePath(Arg.Any<string[]>()).Returns(@"C:\dest\file.czc");
        _obfuscationStrategy
            .ObfuscateFileName(Arg.Any<string>(), Arg.Any<string>())
            .Returns("file.czc");
        _encryptionStrategy
            .EncryptFileAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<KeyDerivationAlgorithm>(),
                Arg.Any<CompressionMode>()
            )
            .Returns(true);

        Result<FileCryptResult> result = await _service.ProcessAsync(
            @"C:\source\file.txt",
            @"C:\dest\file.czc",
            CreateRequest(),
            _progress,
            CancellationToken.None
        );

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.IsSuccess, Is.True);
        Assert.That(result.Value.ProcessedFiles, Is.EqualTo(1));
        Assert.That(result.Value.TotalFiles, Is.EqualTo(1));
    }

    [Test]
    public async Task ProcessAsync_Decrypt_SuccessfulDecryption_ReturnsSuccess()
    {
        _fileOps.GetFileSize(Arg.Any<string>()).Returns(200L);
        _encryptionStrategy
            .DecryptFileAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<KeyDerivationAlgorithm>()
            )
            .Returns(true);

        Result<FileCryptResult> result = await _service.ProcessAsync(
            @"C:\source\file.czc",
            @"C:\dest\file.txt",
            CreateRequest(EncryptOperation.Decrypt),
            _progress,
            CancellationToken.None
        );

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.IsSuccess, Is.True);
    }

    [Test]
    public async Task ProcessAsync_Decrypt_ManifestFile_IgnoresAndReturnsSuccess()
    {
        string manifestPath = @$"C:\source\{FileCryptConstants.ManifestFileName}";

        Result<FileCryptResult> result = await _service.ProcessAsync(
            manifestPath,
            @"C:\dest\manifest",
            CreateRequest(EncryptOperation.Decrypt),
            _progress,
            CancellationToken.None
        );

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.ProcessedFiles, Is.EqualTo(0));
    }

    [Test]
    public async Task ProcessAsync_EncryptionException_ReturnsFailure()
    {
        _fileOps.GetFileSize(Arg.Any<string>()).Returns(100L);
        _fileOps.GetDirectoryName(Arg.Any<string>()).Returns(@"C:\dest");
        _fileOps.CombinePath(Arg.Any<string[]>()).Returns(@"C:\dest\file.czc");
        _obfuscationStrategy
            .ObfuscateFileName(Arg.Any<string>(), Arg.Any<string>())
            .Returns("file.czc");
        _encryptionStrategy
            .EncryptFileAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<KeyDerivationAlgorithm>(),
                Arg.Any<CompressionMode>()
            )
            .ThrowsAsync(new EncryptionInvalidPasswordException());

        Result<FileCryptResult> result = await _service.ProcessAsync(
            @"C:\source\file.txt",
            @"C:\dest\file.czc",
            CreateRequest(),
            _progress,
            CancellationToken.None
        );

        Assert.That(result.IsSuccess, Is.False);
    }

    [Test]
    public async Task ProcessAsync_ReportsProgress()
    {
        _fileOps.GetFileSize(Arg.Any<string>()).Returns(100L);
        _fileOps.GetDirectoryName(Arg.Any<string>()).Returns(@"C:\dest");
        _fileOps.CombinePath(Arg.Any<string[]>()).Returns(@"C:\dest\file.czc");
        _obfuscationStrategy
            .ObfuscateFileName(Arg.Any<string>(), Arg.Any<string>())
            .Returns("file.czc");
        _encryptionStrategy
            .EncryptFileAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<KeyDerivationAlgorithm>(),
                Arg.Any<CompressionMode>()
            )
            .Returns(true);

        await _service.ProcessAsync(
            @"C:\source\file.txt",
            @"C:\dest\file.czc",
            CreateRequest(),
            _progress,
            CancellationToken.None
        );

        _progress.Received(2).Report(Arg.Any<FileCryptStatus>());
    }

    [Test]
    public async Task ProcessAsync_Encrypt_AppliesObfuscation()
    {
        _fileOps.GetFileSize(Arg.Any<string>()).Returns(100L);
        _fileOps.GetDirectoryName(Arg.Any<string>()).Returns(@"C:\dest");
        _fileOps.CombinePath(Arg.Any<string[]>()).Returns(@"C:\dest\obfuscated.czc");
        _obfuscationStrategy
            .ObfuscateFileName(Arg.Any<string>(), Arg.Any<string>())
            .Returns("obfuscated.czc");
        _encryptionStrategy
            .EncryptFileAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<KeyDerivationAlgorithm>(),
                Arg.Any<CompressionMode>()
            )
            .Returns(true);

        await _service.ProcessAsync(
            @"C:\source\file.txt",
            @"C:\dest\file.czc",
            CreateRequest(),
            _progress,
            CancellationToken.None
        );

        _obfuscationStrategy.Received(1).ObfuscateFileName(Arg.Any<string>(), Arg.Any<string>());
    }
}
