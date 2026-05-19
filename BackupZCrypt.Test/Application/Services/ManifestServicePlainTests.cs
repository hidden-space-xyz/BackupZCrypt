namespace BackupZCrypt.Test.Application.Services;

using BackupZCrypt.Application.Services;
using BackupZCrypt.Application.ValueObjects.Manifest;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Infrastructure.Services.FileSystem;
using System.Text.Json;

[TestFixture]
internal sealed class ManifestServicePlainTests
{
    private ManifestService service = null!;

    [SetUp]
    public void SetUp()
    {
        this.service = new ManifestService(
            new FileOperationsService(),
            Array.Empty<IEncryptionAlgorithmStrategy>());
    }

    [Test]
    public async Task TrySavePlainManifestAsync_WritesJsonFile()
    {
        var testDir = CreateTestDirectory();

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
                CompressionMode.Zstd);

            var errors = await this.service.TrySavePlainManifestAsync(
                entries,
                header,
                testDir,
                CancellationToken.None);

            var manifestPath = Path.Combine(testDir, "manifest.bzc");

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
            DeleteTestDirectory(testDir);
        }
    }

    [Test]
    public async Task TrySavePlainManifestAsync_EmptyEntries_ReturnsNoErrors()
    {
        ManifestHeader header = new(
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.Argon2id,
            CompressionMode.None);

        var errors = await this.service.TrySavePlainManifestAsync(
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
            CompressionMode.None);

        var errors = await this.service.TrySavePlainManifestAsync(
            entries,
            header,
            Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "missing-parent",
                Guid.NewGuid().ToString("N"),
                "sub"),
            CancellationToken.None);

        Assert.That(errors, Has.Count.EqualTo(1));
    }

    private static string CreateTestDirectory()
    {
        var path = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "manifest-plain-tests",
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
