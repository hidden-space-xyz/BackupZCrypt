namespace BackupZCrypt.Test.Application.Services;

using BackupZCrypt.Application.Services;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Application.ValueObjects.Manifest;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Factories.Interfaces;
using BackupZCrypt.Domain.Services.Interfaces;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Backup;
using BackupZCrypt.Domain.ValueObjects.Encryption;
using NSubstitute;

[TestFixture]
internal sealed class DirectoryBackupServiceTests
{
    private IEncryptionServiceFactory encryptionFactory = null!;
    private ICompressionServiceFactory compressionFactory = null!;
    private INameObfuscationServiceFactory obfuscationFactory = null!;
    private IFileOperationsService fileOps = null!;
    private IManifestService manifestService = null!;
    private IEncryptionAlgorithmStrategy encryptionStrategy = null!;
    private IProgress<BackupStatus> progress = null!;
    private DirectoryBackupService service = null!;

    [SetUp]
    public void SetUp()
    {
        this.encryptionFactory = Substitute.For<IEncryptionServiceFactory>();
        this.compressionFactory = Substitute.For<ICompressionServiceFactory>();
        this.obfuscationFactory = Substitute.For<INameObfuscationServiceFactory>();
        this.fileOps = Substitute.For<IFileOperationsService>();
        this.manifestService = Substitute.For<IManifestService>();
        this.encryptionStrategy = Substitute.For<IEncryptionAlgorithmStrategy>();

        this.encryptionFactory.Create(Arg.Any<EncryptionAlgorithm>())
            .Returns(this.encryptionStrategy);
        this.encryptionStrategy.Id.Returns(EncryptionAlgorithm.Aes);

        this.progress = Substitute.For<IProgress<BackupStatus>>();

        this.service = new DirectoryBackupService(
            this.encryptionFactory,
            this.compressionFactory,
            this.obfuscationFactory,
            this.fileOps,
            this.manifestService,
            [this.encryptionStrategy]);
    }

    [Test]
    public async Task Decrypt_Encrypted_NoManifest_ReturnsFailure()
    {
        this.manifestService
            .TryReadManifestAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<IEncryptionAlgorithmStrategy>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns((ManifestData?)null);

        Result<BackupResult> result = await service.ProcessAsync(
            @"C:\source",
            @"C:\dest",
            CreateRequest(EncryptOperation.Decrypt),
            progress,
            CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
    }

    [Test]
    public async Task Decrypt_NonEncrypted_NoManifest_ReturnsFailure()
    {
        this.manifestService
            .TryReadManifestAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<IEncryptionAlgorithmStrategy>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns((ManifestData?)null);

        BackupRequest request = new(
            @"C:\source",
            @"C:\dest",
            string.Empty,
            string.Empty,
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.Argon2id,
            EncryptOperation.Decrypt,
            NameObfuscationMode.None,
            CompressionMode.Zstd,
            UseEncryption: false);

        Result<BackupResult> result = await service.ProcessAsync(
            @"C:\source",
            @"C:\dest",
            request,
            progress,
            CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
    }

    [Test]
    public async Task Decrypt_WithManifest_OverridesRequestAlgorithms()
    {
        ManifestData manifestData = new(
            new ManifestHeader(
                EncryptionAlgorithm.ChaCha20,
                KeyDerivationAlgorithm.Scrypt,
                NameObfuscationMode.None,
                CompressionMode.ZstdBest),
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

        IEncryptionAlgorithmStrategy chacha = Substitute.For<IEncryptionAlgorithmStrategy>();
        chacha.Id.Returns(EncryptionAlgorithm.ChaCha20);
        this.encryptionFactory.Create(EncryptionAlgorithm.ChaCha20).Returns(chacha);
        chacha.DecryptFileAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<KeyDerivationAlgorithm>(),
                Arg.Any<EncryptionMetadata>(),
                Arg.Any<CancellationToken>())
            .Returns(true);

        this.fileOps.GetFilesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([@"C:\source\file.bzc"]);
        this.fileOps.GetRelativePath(Arg.Any<string>(), Arg.Any<string>())
            .Returns(callInfo =>
            {
                string basePath = callInfo.ArgAt<string>(0);
                string fullPath = callInfo.ArgAt<string>(1);
                return Path.GetRelativePath(basePath, fullPath);
            });
        this.fileOps.GetDirectoryName(Arg.Any<string>()).Returns(@"C:\dest");
        this.fileOps.CombinePath(Arg.Any<string[]>())
            .Returns(callInfo => Path.Combine(callInfo.ArgAt<string[]>(0)));
        this.fileOps.GetFileSize(Arg.Any<string>()).Returns(100L);

        await service.ProcessAsync(
            @"C:\source",
            @"C:\dest",
            CreateRequest(EncryptOperation.Decrypt),
            progress,
            CancellationToken.None);

        this.encryptionFactory.Received().Create(EncryptionAlgorithm.ChaCha20);
    }

    [Test]
    public async Task Encrypt_EmptyDirectory_ReturnsNoFilesError()
    {
        this.fileOps.GetFilesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<string>());

        Result<BackupResult> result = await service.ProcessAsync(
            @"C:\source",
            @"C:\dest",
            CreateRequest(EncryptOperation.Encrypt),
            progress,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.HasErrors, Is.True);
            Assert.That(result.Value.TotalFiles, Is.Zero);
        }
    }

    [Test]
    public async Task Encrypt_WithFiles_CreatesManifestEntries()
    {
        this.fileOps.GetFilesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([@"C:\source\file1.txt", @"C:\source\file2.txt"]);
        this.fileOps.GetRelativePath(Arg.Any<string>(), Arg.Any<string>())
            .Returns(callInfo =>
            {
                string basePath = callInfo.ArgAt<string>(0);
                string fullPath = callInfo.ArgAt<string>(1);
                return Path.GetRelativePath(basePath, fullPath);
            });
        this.fileOps.GetDirectoryName(Arg.Any<string>()).Returns(@"C:\dest");
        this.fileOps.CombinePath(Arg.Any<string[]>())
            .Returns(callInfo => Path.Combine(callInfo.ArgAt<string[]>(0)));
        this.fileOps.GetFileSize(Arg.Any<string>()).Returns(100L);

        this.encryptionStrategy
            .EncryptFileAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<KeyDerivationAlgorithm>(),
                Arg.Any<CompressionMode>(),
                Arg.Any<CancellationToken>())
            .Returns(new EncryptionMetadata(new byte[32], new byte[12], CompressionMode.None));

        this.manifestService
            .TrySaveManifestAsync(
                Arg.Any<IReadOnlyList<ManifestEntry>>(),
                Arg.Any<ManifestHeader>(),
                Arg.Any<string>(),
                Arg.Any<IEncryptionAlgorithmStrategy>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<string>());

        await service.ProcessAsync(
            @"C:\source",
            @"C:\dest",
            CreateRequest(EncryptOperation.Encrypt),
            progress,
            CancellationToken.None);

        await this.manifestService.Received(1).TrySaveManifestAsync(
            Arg.Is<IReadOnlyList<ManifestEntry>>(e => e.Count == 2),
            Arg.Any<ManifestHeader>(),
            Arg.Any<string>(),
            Arg.Any<IEncryptionAlgorithmStrategy>(),
            Arg.Any<BackupRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Decrypt_SkipsManifestFile()
    {
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

        this.fileOps.GetFilesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([@"C:\source\file.bzc", @"C:\source\manifest.bzc"]);
        this.fileOps.GetRelativePath(@"C:\source", @"C:\source\file.bzc").Returns("file.bzc");
        this.fileOps.GetRelativePath(@"C:\source", @"C:\source\manifest.bzc").Returns("manifest.bzc");
        this.fileOps.GetDirectoryName(Arg.Any<string>()).Returns(@"C:\dest");
        this.fileOps.CombinePath(Arg.Any<string[]>())
            .Returns(callInfo => Path.Combine(callInfo.ArgAt<string[]>(0)));
        this.fileOps.GetFileSize(Arg.Any<string>()).Returns(100L);

        this.encryptionStrategy
            .DecryptFileAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<KeyDerivationAlgorithm>(),
                Arg.Any<EncryptionMetadata>(),
                Arg.Any<CancellationToken>())
            .Returns(true);

        Result<BackupResult> result = await service.ProcessAsync(
            @"C:\source",
            @"C:\dest",
            CreateRequest(EncryptOperation.Decrypt),
            progress,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.TotalFiles, Is.EqualTo(1));
        }
    }

    private static BackupRequest CreateRequest(
        EncryptOperation operation = EncryptOperation.Encrypt) =>
        new(
            @"C:\source",
            @"C:\dest",
            "StrongP@ss1",
            "StrongP@ss1",
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.Argon2id,
            operation,
            NameObfuscationMode.None);
}
