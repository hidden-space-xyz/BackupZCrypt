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
        string tempDir = Path.Combine(Path.GetTempPath(), $"bzc-test-{Guid.NewGuid():N}");
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

            IReadOnlyList<string> errors = await service.TrySavePlainManifestAsync(
                entries,
                header,
                tempDir,
                CancellationToken.None);

            string manifestPath = Path.Combine(tempDir, "manifest.bzc");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(errors, Is.Empty);
                Assert.That(File.Exists(manifestPath), Is.True);
            }

            string json = await File.ReadAllTextAsync(manifestPath);
            ManifestDocument? doc = JsonSerializer.Deserialize<ManifestDocument>(json);

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

        IReadOnlyList<string> errors = await service.TrySavePlainManifestAsync(
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

        IReadOnlyList<string> errors = await service.TrySavePlainManifestAsync(
            entries,
            header,
            Path.Combine(Path.GetTempPath(), $"nonexistent-{Guid.NewGuid():N}", "sub"),
            CancellationToken.None);

        Assert.That(errors, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task TryReadManifestAsync_PlainJsonManifest_ParsesCorrectly()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"bzc-test-{Guid.NewGuid():N}");
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

            byte[] jsonBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(document));
            string manifestPath = Path.Combine(tempDir, "manifest.bzc");
            await File.WriteAllBytesAsync(manifestPath, jsonBytes);

            IEncryptionAlgorithmStrategy mockStrategy =
                Substitute.For<IEncryptionAlgorithmStrategy>();
            mockStrategy.Id.Returns(EncryptionAlgorithm.ChaCha20);

            ManifestData? result = await service.TryReadManifestAsync(
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
        string tempDir = Path.Combine(Path.GetTempPath(), $"bzc-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string manifestPath = Path.Combine(tempDir, "manifest.bzc");
            await File.WriteAllTextAsync(manifestPath, "{not valid json!!!");

            IEncryptionAlgorithmStrategy mockStrategy =
                Substitute.For<IEncryptionAlgorithmStrategy>();

            ManifestData? result = await service.TryReadManifestAsync(
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
        string tempDir = Path.Combine(Path.GetTempPath(), $"bzc-test-{Guid.NewGuid():N}");
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

            IReadOnlyList<string> saveErrors = await service.TrySavePlainManifestAsync(
                entries,
                header,
                tempDir,
                CancellationToken.None);

            Assert.That(saveErrors, Is.Empty);

            IEncryptionAlgorithmStrategy mockStrategy =
                Substitute.For<IEncryptionAlgorithmStrategy>();

            ManifestData? result = await service.TryReadManifestAsync(
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
