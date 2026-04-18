namespace BackupZCrypt.Test.Application.Services;

using BackupZCrypt.Application.Services;
using BackupZCrypt.Application.ValueObjects.Manifest;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Backup;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using System.Text.Json;

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
        string tempDir = Path.Combine(Path.GetTempPath(), $"bzc-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string manifestPath = Path.Combine(tempDir, "manifest.bzc");
            byte[] preamble = [(byte)EncryptionAlgorithm.Aes, (byte)KeyDerivationAlgorithm.Argon2id];
            byte[] fakeEncryptedContent = [1, 2, 3];
            await File.WriteAllBytesAsync(manifestPath, [.. preamble, .. fakeEncryptedContent]);

            IEncryptionAlgorithmStrategy encryptionService =
                Substitute.For<IEncryptionAlgorithmStrategy>();
            encryptionService.Id.Returns(EncryptionAlgorithm.Aes);
            encryptionService
                .ReadEncryptedDataAsync(
                    Arg.Any<ReadOnlyMemory<byte>>(),
                    Arg.Any<string>(),
                    Arg.Any<KeyDerivationAlgorithm>())
                .ThrowsAsync(new InvalidOperationException("decryption failed"));

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
    public async Task TryReadManifestAsync_EncryptedManifest_ParsesDecryptedPayload()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"bzc-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string manifestPath = Path.Combine(tempDir, "manifest.bzc");
            await File.WriteAllBytesAsync(
                manifestPath,
                [(byte)EncryptionAlgorithm.Aes, (byte)KeyDerivationAlgorithm.PBKDF2, 9, 8, 7]);

            ManifestDocument document = new(
                EncryptionAlgorithm.Aes,
                KeyDerivationAlgorithm.PBKDF2,
                NameObfuscationMode.None,
                CompressionMode.None,
                [new ManifestEntry("file.bzc", "file.txt", "c2FsdA==", "bm9uY2U=", string.Empty)]);

            IEncryptionAlgorithmStrategy encryptionService =
                Substitute.For<IEncryptionAlgorithmStrategy>();
            encryptionService.Id.Returns(EncryptionAlgorithm.Aes);
            encryptionService
                .ReadEncryptedDataAsync(
                    Arg.Any<ReadOnlyMemory<byte>>(),
                    Arg.Any<string>(),
                    Arg.Any<KeyDerivationAlgorithm>(),
                    Arg.Any<CancellationToken>())
                .Returns(JsonSerializer.SerializeToUtf8Bytes(document));

            ManifestData? result = await service.TryReadManifestAsync(
                tempDir,
                [encryptionService],
                "StrongP@ss1",
                CancellationToken.None);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(result, Is.Not.Null);
                Assert.That(result!.Header.KeyDerivationAlgorithm, Is.EqualTo(KeyDerivationAlgorithm.PBKDF2));
                Assert.That(result.FileMap.ContainsKey("file.bzc"), Is.True);
                Assert.That(result.FileMap["file.bzc"].OriginalRelativePath, Is.EqualTo("file.txt"));
            }
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
        string tempDir = Path.Combine(Path.GetTempPath(), $"bzc-test-{Guid.NewGuid():N}");
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
    public async Task TryReadManifestAsync_FileTooShortForPreamble_ReturnsNull()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"bzc-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string manifestPath = Path.Combine(tempDir, "manifest.bzc");
            await File.WriteAllBytesAsync(manifestPath, [1]);

            IEncryptionAlgorithmStrategy encryptionService =
                Substitute.For<IEncryptionAlgorithmStrategy>();

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
    public async Task TryReadManifestAsync_NoMatchingStrategy_ReturnsNull()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"bzc-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string manifestPath = Path.Combine(tempDir, "manifest.bzc");
            byte[] preamble = [(byte)EncryptionAlgorithm.ChaCha20, (byte)KeyDerivationAlgorithm.Argon2id];
            await File.WriteAllBytesAsync(manifestPath, [.. preamble, 1, 2, 3]);

            IEncryptionAlgorithmStrategy encryptionService =
                Substitute.For<IEncryptionAlgorithmStrategy>();
            encryptionService.Id.Returns(EncryptionAlgorithm.Aes);

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
            .CreateEncryptedDataAsync(
                Arg.Any<byte[]>(),
                Arg.Any<string>(),
                Arg.Any<KeyDerivationAlgorithm>(),
                Arg.Any<CompressionMode>())
            .ThrowsAsync(new InvalidOperationException("encryption failed"));

        string tempDir = Path.Combine(Path.GetTempPath(), $"bzc-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            List<ManifestEntry> entries = [new("obfuscated.bzc", "original.txt", "c2FsdA==", "bm9uY2U=", string.Empty)];

            IReadOnlyList<string> errors = await service.TrySaveManifestAsync(
                entries,
                CreateHeader(),
                tempDir,
                encryptionService,
                CreateRequest(),
                CancellationToken.None);

            Assert.That(errors, Has.Count.EqualTo(1));
            Assert.That(errors[0], Does.Contain("encryption failed"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task TrySaveManifestAsync_WritesPreambleAndEncryptedPayload()
    {
        IEncryptionAlgorithmStrategy encryptionService =
            Substitute.For<IEncryptionAlgorithmStrategy>();
        encryptionService
            .CreateEncryptedDataAsync(
                Arg.Any<byte[]>(),
                Arg.Any<string>(),
                Arg.Any<KeyDerivationAlgorithm>(),
                Arg.Any<CompressionMode>(),
                Arg.Any<CancellationToken>())
            .Returns([3, 4, 5]);

        string tempDir = Path.Combine(Path.GetTempPath(), $"bzc-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            List<ManifestEntry> entries = [new("obfuscated.bzc", "original.txt", "c2FsdA==", "bm9uY2U=", string.Empty)];

            IReadOnlyList<string> errors = await service.TrySaveManifestAsync(
                entries,
                CreateHeader(),
                tempDir,
                encryptionService,
                CreateRequest(),
                CancellationToken.None);

            byte[] manifestBytes = await File.ReadAllBytesAsync(Path.Combine(tempDir, "manifest.bzc"));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(errors, Is.Empty);
                Assert.That(
                    manifestBytes,
                    Is.EqualTo(
                        new byte[]
                        {
                            (byte)EncryptionAlgorithm.Aes,
                            (byte)KeyDerivationAlgorithm.Argon2id,
                            3,
                            4,
                            5,
                        }));
            }
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
            .CreateEncryptedDataAsync(
                Arg.Any<byte[]>(),
                Arg.Any<string>(),
                Arg.Any<KeyDerivationAlgorithm>(),
                Arg.Any<CompressionMode>())
                .ThrowsAsync(new IOException("disk error"));

        string tempDir = Path.Combine(Path.GetTempPath(), $"bzc-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            List<ManifestEntry> entries = [new("b.bzc", "a.txt", "c2FsdA==", "bm9uY2U=", string.Empty)];

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

    private static BackupRequest CreateRequest() =>
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
