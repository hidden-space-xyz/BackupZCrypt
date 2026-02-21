namespace BackupZCrypt.Test.Application.Services;

using System.Text;
using BackupZCrypt.Application.Services;
using BackupZCrypt.Application.ValueObjects.Manifest;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Domain.ValueObjects.FileCrypt;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

[TestFixture]
internal sealed class ManifestServiceTests
{
    private ManifestService service = null!;

    [SetUp]
    public void SetUp()
    {
        this.service = new ManifestService();
    }

    [Test]
    public async Task TryReadManifestAsync_DecryptionFails_ReturnsNull()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"czc-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string manifestPath = Path.Combine(tempDir, "manifest.czc");
            await File.WriteAllBytesAsync(manifestPath, [1, 2, 3]);

            IEncryptionAlgorithmStrategy encryptionService =
                Substitute.For<IEncryptionAlgorithmStrategy>();
            encryptionService
                .DecryptFileAsync(
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<KeyDerivationAlgorithm>())
                .Returns(false);

            ManifestData? result = await service.TryReadManifestAsync(
                tempDir,
                [encryptionService],
                "StrongP@ss1",
                CancellationToken.None);

            Assert.That(result, Is.Null);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task TryReadManifestAsync_ManifestDoesNotExist_ReturnsNull()
    {
        IEncryptionAlgorithmStrategy encryptionService =
            Substitute.For<IEncryptionAlgorithmStrategy>();
        string tempDir = Path.Combine(Path.GetTempPath(), $"czc-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            ManifestData? result = await service.TryReadManifestAsync(
                tempDir,
                [encryptionService],
                "StrongP@ss1",
                CancellationToken.None);

            Assert.That(result, Is.Null);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task TrySaveManifestAsync_EmptyEntries_ReturnsNoErrors()
    {
        IEncryptionAlgorithmStrategy encryptionService =
            Substitute.For<IEncryptionAlgorithmStrategy>();

        IReadOnlyList<string> errors = await service.TrySaveManifestAsync(
            [],
            CreateHeader(),
            @"C:\dest",
            encryptionService,
            CreateRequest(),
            CancellationToken.None);

        Assert.That(errors, Is.Empty);
    }

    [Test]
    public async Task TrySaveManifestAsync_EncryptionFails_ReturnsError()
    {
        IEncryptionAlgorithmStrategy encryptionService =
            Substitute.For<IEncryptionAlgorithmStrategy>();
        encryptionService
            .CreateEncryptedFileAsync(
                Arg.Any<byte[]>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<KeyDerivationAlgorithm>(),
                Arg.Any<CompressionMode>())
            .Returns(false);

        string tempDir = Path.Combine(Path.GetTempPath(), $"czc-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            List<ManifestEntry> entries = [new("original.txt", "obfuscated.czc")];

            IReadOnlyList<string> errors = await service.TrySaveManifestAsync(
                entries,
                CreateHeader(),
                tempDir,
                encryptionService,
                CreateRequest(),
                CancellationToken.None);

            Assert.That(errors, Has.Count.EqualTo(1));
            Assert.That(errors[0], Does.Contain("Failed"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task TrySaveManifestAsync_ExceptionThrown_ReturnsError()
    {
        IEncryptionAlgorithmStrategy encryptionService =
            Substitute.For<IEncryptionAlgorithmStrategy>();
        encryptionService
            .CreateEncryptedFileAsync(
                Arg.Any<byte[]>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<KeyDerivationAlgorithm>(),
                Arg.Any<CompressionMode>())
            .ThrowsAsync(new IOException("disk error"));

        string tempDir = Path.Combine(Path.GetTempPath(), $"czc-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            List<ManifestEntry> entries = [new("a.txt", "b.czc")];

            IReadOnlyList<string> errors = await service.TrySaveManifestAsync(
                entries,
                CreateHeader(),
                tempDir,
                encryptionService,
                CreateRequest(),
                CancellationToken.None);

            Assert.That(errors, Has.Count.EqualTo(1));
            Assert.That(errors[0], Does.Contain("disk error"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    private static ManifestHeader CreateHeader() =>
        new(
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.Argon2id,
            NameObfuscationMode.None,
            CompressionMode.None);

    private static FileCryptRequest CreateRequest() =>
        new(
            @"C:\source",
            @"C:\dest",
            "StrongP@ss1",
            "StrongP@ss1",
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.Argon2id,
            EncryptOperation.Encrypt,
            NameObfuscationMode.None);
}
