using System.Text;
using CloudZCrypt.Application.Services;
using CloudZCrypt.Application.ValueObjects.Manifest;
using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Strategies.Interfaces;
using CloudZCrypt.Domain.ValueObjects.FileCrypt;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CloudZCrypt.Test.Application.Services;

[TestFixture]
internal sealed class ManifestServiceTests
{
    private ManifestService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _service = new ManifestService();
    }

    private static FileCryptRequest CreateRequest() =>
        new(
            @"C:\source", @"C:\dest",
            "StrongP@ss1", "StrongP@ss1",
            EncryptionAlgorithm.Aes, KeyDerivationAlgorithm.Argon2id,
            EncryptOperation.Encrypt, NameObfuscationMode.None
        );

    [Test]
    public async Task TryReadManifestAsync_ManifestDoesNotExist_ReturnsNull()
    {
        IEncryptionAlgorithmStrategy encryptionService =
            Substitute.For<IEncryptionAlgorithmStrategy>();
        string tempDir = Path.Combine(Path.GetTempPath(), $"czc-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            Dictionary<string, string>? result = await _service.TryReadManifestAsync(
                tempDir, encryptionService, CreateRequest(), CancellationToken.None);

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

        IReadOnlyList<string> errors = await _service.TrySaveManifestAsync(
            [], @"C:\dest", encryptionService, CreateRequest(), CancellationToken.None);

        Assert.That(errors, Is.Empty);
    }

    [Test]
    public async Task TrySaveManifestAsync_EncryptionFails_ReturnsError()
    {
        IEncryptionAlgorithmStrategy encryptionService =
            Substitute.For<IEncryptionAlgorithmStrategy>();
        encryptionService.CreateEncryptedFileAsync(
                Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<KeyDerivationAlgorithm>(), Arg.Any<CompressionMode>())
            .Returns(false);

        string tempDir = Path.Combine(Path.GetTempPath(), $"czc-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            List<ManifestEntry> entries = [new("original.txt", "obfuscated.czc")];

            IReadOnlyList<string> errors = await _service.TrySaveManifestAsync(
                entries, tempDir, encryptionService, CreateRequest(), CancellationToken.None);

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
        encryptionService.CreateEncryptedFileAsync(
                Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<KeyDerivationAlgorithm>(), Arg.Any<CompressionMode>())
            .ThrowsAsync(new IOException("disk error"));

        string tempDir = Path.Combine(Path.GetTempPath(), $"czc-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            List<ManifestEntry> entries = [new("a.txt", "b.czc")];

            IReadOnlyList<string> errors = await _service.TrySaveManifestAsync(
                entries, tempDir, encryptionService, CreateRequest(), CancellationToken.None);

            Assert.That(errors, Has.Count.EqualTo(1));
            Assert.That(errors[0], Does.Contain("disk error"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
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
            encryptionService.DecryptFileAsync(
                    Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                    Arg.Any<KeyDerivationAlgorithm>())
                .Returns(false);

            Dictionary<string, string>? result = await _service.TryReadManifestAsync(
                tempDir, encryptionService, CreateRequest(), CancellationToken.None);

            Assert.That(result, Is.Null);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
