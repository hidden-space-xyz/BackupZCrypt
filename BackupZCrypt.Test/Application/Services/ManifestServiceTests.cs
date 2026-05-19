namespace BackupZCrypt.Test.Application.Services;

using BackupZCrypt.Application.Services;
using BackupZCrypt.Application.ValueObjects.Manifest;
using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Services.Interfaces;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Infrastructure.Services.FileSystem;
using BackupZCrypt.Infrastructure.Strategies.Encryption.Algorithms;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

[TestFixture]
internal sealed class ManifestServiceTests
{
    private ManifestService service = null!;

    [SetUp]
    public void SetUp()
    {
        this.service = new ManifestService(
            new FileOperationsService(),
            CreateEncryptionStrategies());
    }

    [Test]
    public async Task ReadChunkManifestPreambleAsync_NoFile_ReturnsNull()
    {
        var testDir = CreateTestDirectory();

        try
        {
            var result = await this.service.ReadChunkManifestPreambleAsync(
                testDir,
                CancellationToken.None);

            Assert.That(result, Is.Null);
        }
        finally
        {
            DeleteTestDirectory(testDir);
        }
    }

    [Test]
    public async Task ReadChunkManifestPreambleAsync_FileTooSmall_ReturnsNull()
    {
        var testDir = CreateTestDirectory();

        try
        {
            var manifestPath = Path.Combine(testDir, BackupConstants.ManifestFileName);
            await File.WriteAllBytesAsync(manifestPath, new byte[46]);

            var result = await this.service.ReadChunkManifestPreambleAsync(
                testDir,
                CancellationToken.None);

            Assert.That(result, Is.Null);
        }
        finally
        {
            DeleteTestDirectory(testDir);
        }
    }

    [Test]
    public async Task ReadChunkManifestPreambleAsync_ValidFile_ReturnsPreamble()
    {
        var testDir = CreateTestDirectory();

        try
        {
            var manifestPath = Path.Combine(testDir, BackupConstants.ManifestFileName);
            byte[] masterSalt = Enumerable.Range(0, 32).Select(static i => (byte)i).ToArray();
            byte[] nonce = Enumerable.Range(32, 12).Select(static i => (byte)i).ToArray();
            byte[] encryptedPayload = [9, 8, 7, 6];
            byte[] preamble =
            [
                (byte)EncryptionAlgorithm.Aes,
                (byte)KeyDerivationAlgorithm.Scrypt,
                .. masterSalt,
                .. nonce,
                .. encryptedPayload,
            ];

            await File.WriteAllBytesAsync(manifestPath, preamble);

            var result = await this.service.ReadChunkManifestPreambleAsync(
                testDir,
                CancellationToken.None);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(result, Is.Not.Null);
                Assert.That(result!.Algorithm, Is.EqualTo(EncryptionAlgorithm.Aes));
                Assert.That(result.KeyDerivation, Is.EqualTo(KeyDerivationAlgorithm.Scrypt));
                Assert.That(result.MasterSalt, Is.EqualTo(masterSalt));
                Assert.That(result.Nonce, Is.EqualTo(nonce));
                Assert.That(result.EncryptedPayload, Is.EqualTo(encryptedPayload));
            }
        }
        finally
        {
            DeleteTestDirectory(testDir);
        }
    }

    [Test]
    public async Task DecryptChunkManifest_InvalidKey_ReturnsNull()
    {
        var testDir = CreateTestDirectory();

        try
        {
            byte[] encryptionKey = Enumerable.Range(0, 32).Select(static i => (byte)(i + 1)).ToArray();
            byte[] invalidKey = Enumerable.Range(0, 32).Select(static i => (byte)(i + 2)).ToArray();

            var errors = await this.service.SaveChunkManifestAsync(
                CreateManifestData(),
                testDir,
                encryptionKey,
                EncryptionAlgorithm.Aes,
                CancellationToken.None);

            Assert.That(errors, Is.Empty);

            var preamble = await this.service.ReadChunkManifestPreambleAsync(
                testDir,
                CancellationToken.None);

            var manifest = this.service.DecryptChunkManifest(preamble!, invalidKey);

            Assert.That(manifest, Is.Null);
        }
        finally
        {
            DeleteTestDirectory(testDir);
        }
    }

    [Test]
    public async Task SaveAndRead_RoundTrip()
    {
        var testDir = CreateTestDirectory();

        try
        {
            byte[] encryptionKey = Enumerable.Range(0, 32).Select(static i => (byte)(255 - i)).ToArray();
            var expected = CreateManifestData();

            var saveErrors = await this.service.SaveChunkManifestAsync(
                expected,
                testDir,
                encryptionKey,
                EncryptionAlgorithm.Aes,
                CancellationToken.None);

            var preamble = await this.service.ReadChunkManifestPreambleAsync(
                testDir,
                CancellationToken.None);
            var manifest = this.service.DecryptChunkManifest(preamble!, encryptionKey);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(saveErrors, Is.Empty);
                Assert.That(preamble, Is.Not.Null);
                Assert.That(preamble!.Algorithm, Is.EqualTo(EncryptionAlgorithm.Aes));
                Assert.That(preamble.KeyDerivation, Is.EqualTo(KeyDerivationAlgorithm.PBKDF2));
                Assert.That(preamble.MasterSalt, Is.EqualTo(Convert.FromBase64String(expected.MasterSalt)));
                Assert.That(preamble.Nonce, Has.Length.EqualTo(12));
                Assert.That(manifest, Is.Not.Null);
                Assert.That(manifest!.Header.EncryptionAlgorithm, Is.EqualTo(EncryptionAlgorithm.Aes));
                Assert.That(manifest.Header.KeyDerivationAlgorithm, Is.EqualTo(KeyDerivationAlgorithm.PBKDF2));
                Assert.That(manifest.Header.Compression, Is.EqualTo(CompressionMode.Zstd));
                Assert.That(manifest.MasterSalt, Is.EqualTo(expected.MasterSalt));
                Assert.That(manifest.Files, Has.Count.EqualTo(2));
                Assert.That(manifest.Files[0].OriginalPath, Is.EqualTo("docs\\report.txt"));
                Assert.That(manifest.Files[0].Chunks[0].Hash, Is.EqualTo("chunk-1"));
                Assert.That(manifest.Files[1].OriginalPath, Is.EqualTo("images\\photo.jpg"));
            }
        }
        finally
        {
            DeleteTestDirectory(testDir);
        }
    }

    [Test]
    public async Task SaveChunkManifestAsync_IOError_ReturnsError()
    {
        var fileOperationsService = Substitute.For<IFileOperationsService>();
        fileOperationsService
            .WriteAllBytesAsync(
                Arg.Any<string>(),
                Arg.Any<byte[]>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("disk error"));

        var manifestService = new ManifestService(
            fileOperationsService,
            CreateEncryptionStrategies());

        var errors = await manifestService.SaveChunkManifestAsync(
            CreateManifestData(),
            @"C:\does-not-matter",
            Enumerable.Range(0, 32).Select(static i => (byte)i).ToArray(),
            EncryptionAlgorithm.Aes,
            CancellationToken.None);

        Assert.That(errors, Has.Count.EqualTo(1));
        Assert.That(errors[0], Does.Contain("disk error"));
    }

    private static ChunkManifestData CreateManifestData() =>
        new(
            new ManifestHeader(
                EncryptionAlgorithm.Aes,
                KeyDerivationAlgorithm.PBKDF2,
                CompressionMode.Zstd),
            Convert.ToBase64String(Enumerable.Range(1, 32).Select(static i => (byte)i).ToArray()),
            [
                new ChunkManifestFileEntry(
                    "docs\\report.txt",
                    "file-hash-1",
                    123,
                    [
                        new ChunkManifestChunkRef("chunk-1", 64, Convert.ToBase64String(Enumerable.Range(1, 12).Select(static i => (byte)(10 + i)).ToArray())),
                        new ChunkManifestChunkRef("chunk-2", 59, Convert.ToBase64String(Enumerable.Range(1, 12).Select(static i => (byte)(30 + i)).ToArray())),
                    ]),
                new ChunkManifestFileEntry(
                    "images\\photo.jpg",
                    "file-hash-2",
                    456,
                    [
                        new ChunkManifestChunkRef("chunk-3", 456, Convert.ToBase64String(Enumerable.Range(1, 12).Select(static i => (byte)(50 + i)).ToArray())),
                    ]),
            ]);

    private static IEncryptionAlgorithmStrategy[] CreateEncryptionStrategies() =>
        [new AesEncryptionStrategy()];

    private static string CreateTestDirectory()
    {
        var path = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "manifest-service-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTestDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }
}
