namespace BackupZCrypt.Test.Application.Services;

using BackupZCrypt.Application.Services;
using BackupZCrypt.Application.ValueObjects.Manifest;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Infrastructure.Services.FileSystem;
using NSubstitute;
using System.Text;
using System.Text.Json;

[TestFixture]
internal sealed class ManifestServicePlainTests
{
    private ManifestService service = null!;

    [SetUp]
    public void SetUp()
    {
        this.service = new ManifestService(new FileOperationsService());
    }

    [Test]
    public async Task TrySavePlainManifestAsync_WritesJsonFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"bzc-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            List<ManifestEntry> entries =
            [
                new("file1.txt", "file1.txt", string.Empty, string.Empty, string.Empty),
                new("file2.txt", "file2.txt", string.Empty, string.Empty, string.Empty),
            ];

            ManifestHeader header = new(
                EncryptionAlgorithm.Aes,
                KeyDerivationAlgorithm.Argon2id,
                NameObfuscationMode.None,
                CompressionMode.Zstd);

            var errors = await service.TrySavePlainManifestAsync(
                entries,
                header,
                tempDir,
                CancellationToken.None);

            var manifestPath = Path.Combine(tempDir, "manifest.bzc");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(errors, Is.Empty);
                Assert.That(File.Exists(manifestPath), Is.True);
            }

            var json = await File.ReadAllTextAsync(manifestPath);
            var doc = JsonSerializer.Deserialize<ManifestDocument>(json);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(doc, Is.Not.Null);
                Assert.That(doc!.Entries, Has.Count.EqualTo(2));
                Assert.That(doc.Compression, Is.EqualTo(CompressionMode.Zstd));
                Assert.That(doc.EncryptionAlgorithm, Is.EqualTo(EncryptionAlgorithm.Aes));
            }
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task TrySavePlainManifestAsync_EmptyEntries_ReturnsNoErrors()
    {
        ManifestHeader header = new(
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.Argon2id,
            NameObfuscationMode.None,
            CompressionMode.None);

        var errors = await service.TrySavePlainManifestAsync(
            [],
            header,
            @"C:\nonexistent",
            CancellationToken.None);

        Assert.That(errors, Is.Empty);
    }

    [Test]
    public async Task TrySavePlainManifestAsync_InvalidPath_ReturnsError()
    {
        List<ManifestEntry> entries =
        [
            new("file.txt", "file.txt", string.Empty, string.Empty, string.Empty),
        ];

        ManifestHeader header = new(
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.Argon2id,
            NameObfuscationMode.None,
            CompressionMode.None);

        var errors = await service.TrySavePlainManifestAsync(
            entries,
            header,
            Path.Combine(Path.GetTempPath(), $"nonexistent-{Guid.NewGuid():N}", "sub"),
            CancellationToken.None);

        Assert.That(errors, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task TryReadManifestAsync_PlainJsonManifest_ParsesCorrectly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"bzc-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            ManifestDocument document = new(
                EncryptionAlgorithm.Aes,
                KeyDerivationAlgorithm.Argon2id,
                NameObfuscationMode.Guid,
                CompressionMode.ZstdFast,
                [
                    new ManifestEntry(
                        "abc123.bzc",
                        "original.txt",
                        string.Empty,
                        string.Empty,
                        string.Empty),
                ]);

            var jsonBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(document));
            var manifestPath = Path.Combine(tempDir, "manifest.bzc");
            await File.WriteAllBytesAsync(manifestPath, jsonBytes);

            var mockStrategy =
                Substitute.For<IEncryptionAlgorithmStrategy>();
            mockStrategy.Id.Returns(EncryptionAlgorithm.ChaCha20);

            var result = await service.TryReadManifestAsync(
                tempDir,
                [mockStrategy],
                string.Empty,
                CancellationToken.None);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(result, Is.Not.Null);
                Assert.That(result!.Header.EncryptionAlgorithm, Is.EqualTo(EncryptionAlgorithm.Aes));
                Assert.That(result.Header.Compression, Is.EqualTo(CompressionMode.ZstdFast));
                Assert.That(result.Header.NameObfuscation, Is.EqualTo(NameObfuscationMode.Guid));
                Assert.That(result.FileMap, Has.Count.EqualTo(1));
                Assert.That(result.FileMap.ContainsKey("abc123.bzc"), Is.True);
                Assert.That(
                    result.FileMap["abc123.bzc"].OriginalRelativePath,
                    Is.EqualTo("original.txt"));
            }
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task TryReadManifestAsync_InvalidJson_ReturnsNull()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"bzc-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var manifestPath = Path.Combine(tempDir, "manifest.bzc");
            await File.WriteAllTextAsync(manifestPath, "{not valid json!!!");

            var mockStrategy =
                Substitute.For<IEncryptionAlgorithmStrategy>();

            var result = await service.TryReadManifestAsync(
                tempDir,
                [mockStrategy],
                string.Empty,
                CancellationToken.None);

            Assert.That(result, Is.Null);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task TrySaveThenRead_PlainManifest_RoundTrip()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"bzc-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            List<ManifestEntry> entries =
            [
                new("compressed1.bzc", "document.pdf", string.Empty, string.Empty, string.Empty),
                new("compressed2.bzc", "photo.jpg", string.Empty, string.Empty, string.Empty),
            ];

            ManifestHeader header = new(
                EncryptionAlgorithm.Aes,
                KeyDerivationAlgorithm.PBKDF2,
                NameObfuscationMode.None,
                CompressionMode.ZstdBest);

            var saveErrors = await service.TrySavePlainManifestAsync(
                entries,
                header,
                tempDir,
                CancellationToken.None);

            Assert.That(saveErrors, Is.Empty);

            var mockStrategy =
                Substitute.For<IEncryptionAlgorithmStrategy>();

            var result = await service.TryReadManifestAsync(
                tempDir,
                [mockStrategy],
                string.Empty,
                CancellationToken.None);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(result, Is.Not.Null);
                Assert.That(result!.Header.Compression, Is.EqualTo(CompressionMode.ZstdBest));
                Assert.That(
                    result.Header.KeyDerivationAlgorithm,
                    Is.EqualTo(KeyDerivationAlgorithm.PBKDF2));
                Assert.That(result.FileMap, Has.Count.EqualTo(2));
                Assert.That(
                    result.FileMap["compressed1.bzc"].OriginalRelativePath,
                    Is.EqualTo("document.pdf"));
                Assert.That(
                    result.FileMap["compressed2.bzc"].OriginalRelativePath,
                    Is.EqualTo("photo.jpg"));
            }
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
