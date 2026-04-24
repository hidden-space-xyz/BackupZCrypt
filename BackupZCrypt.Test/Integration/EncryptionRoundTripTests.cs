namespace BackupZCrypt.Test.Integration;

using BackupZCrypt.Composition;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Exceptions;
using BackupZCrypt.Domain.Factories.Interfaces;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Encryption;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography;
using System.Text;

[TestFixture]
internal sealed class EncryptionRoundTripTests
{
    private IEncryptionServiceFactory encryptionFactory = null!;
    private ServiceProvider provider = null!;
    private string testDir = null!;

    [TestCase(EncryptionAlgorithm.Aes)]
    [TestCase(EncryptionAlgorithm.ChaCha20)]
    public async Task CreateEncryptedDataAndReadBack_RoundTrip(EncryptionAlgorithm algorithm)
    {
        var plaintext = Encoding.UTF8.GetBytes("In-memory manifest payload for testing");
        const string password = "TestP@ssw0rd!Str0ng";

        var strategy = this.encryptionFactory.Create(algorithm);

        var encryptedData = await strategy.CreateEncryptedDataAsync(
            plaintext,
            password,
            KeyDerivationAlgorithm.PBKDF2);

        var readBack = await strategy.ReadEncryptedDataAsync(
            encryptedData,
            password,
            KeyDerivationAlgorithm.PBKDF2);

        Assert.That(readBack, Is.EqualTo(plaintext));
    }

    [Test]
    public void DecryptFile_CorruptedFile_ThrowsEncryptionCorruptedFileException()
    {
        var corruptedFile = Path.Combine(this.testDir, "corrupted.bzc");
        File.WriteAllBytes(corruptedFile, [1, 2, 3]);
        var dest = Path.Combine(this.testDir, "out.txt");

        var strategy = this.encryptionFactory.Create(EncryptionAlgorithm.Aes);

        Assert.ThrowsAsync<EncryptionCorruptedFileException>(
            async () =>
                await strategy.DecryptFileAsync(
                    corruptedFile,
                    dest,
                    "Password1234!",
                    KeyDerivationAlgorithm.PBKDF2));
    }

    [Test]
    public void DecryptFile_SourceNotFound_ThrowsEncryptionFileNotFoundException()
    {
        var nonExistent = Path.Combine(this.testDir, "nonexistent.bzc");
        var dest = Path.Combine(this.testDir, "out.txt");

        var strategy = this.encryptionFactory.Create(EncryptionAlgorithm.Aes);

        Assert.ThrowsAsync<EncryptionFileNotFoundException>(
            async () =>
                await strategy.DecryptFileAsync(
                    nonExistent,
                    dest,
                    "Password1234!",
                    KeyDerivationAlgorithm.PBKDF2));
    }

    [Test]
    public async Task DecryptFile_WrongPassword_ThrowsEncryptionInvalidPasswordException()
    {
        var sourceFile = this.CreateTestFile("wrong-pass.txt", "secret data");
        var encryptedFile = Path.Combine(this.testDir, "wrong-pass.bzc");
        var decryptedFile = Path.Combine(this.testDir, "wrong-pass-out.txt");

        var strategy = this.encryptionFactory.Create(EncryptionAlgorithm.Aes);

        var metadata = await strategy.EncryptFileAsync(
            sourceFile,
            encryptedFile,
            "CorrectPassword1!",
            KeyDerivationAlgorithm.PBKDF2);

        Assert.ThrowsAsync<EncryptionInvalidPasswordException>(
            async () =>
                await strategy.DecryptFileAsync(
                    encryptedFile,
                    decryptedFile,
                    "WrongPassword1!!",
                    KeyDerivationAlgorithm.PBKDF2,
                    metadata));
    }

    [TestCase(EncryptionAlgorithm.Aes, KeyDerivationAlgorithm.PBKDF2)]
    [TestCase(EncryptionAlgorithm.Twofish, KeyDerivationAlgorithm.PBKDF2)]
    [TestCase(EncryptionAlgorithm.Serpent, KeyDerivationAlgorithm.PBKDF2)]
    [TestCase(EncryptionAlgorithm.ChaCha20, KeyDerivationAlgorithm.PBKDF2)]
    [TestCase(EncryptionAlgorithm.Camellia, KeyDerivationAlgorithm.PBKDF2)]
    [TestCase(EncryptionAlgorithm.Aes, KeyDerivationAlgorithm.Scrypt)]
    [TestCase(EncryptionAlgorithm.Twofish, KeyDerivationAlgorithm.Scrypt)]
    [TestCase(EncryptionAlgorithm.Serpent, KeyDerivationAlgorithm.Scrypt)]
    [TestCase(EncryptionAlgorithm.ChaCha20, KeyDerivationAlgorithm.Scrypt)]
    [TestCase(EncryptionAlgorithm.Camellia, KeyDerivationAlgorithm.Scrypt)]
    public async Task EncryptAndDecryptFile_AllAlgorithms_PBKDF2_RoundTrip(
        EncryptionAlgorithm algorithm,
        KeyDerivationAlgorithm kdf)
    {
        const string originalContent = "This is a test file for encryption round trip!";
        var sourceFile = this.CreateTestFile("original.txt", originalContent);
        var encryptedFile = Path.Combine(this.testDir, "encrypted.bzc");
        var decryptedFile = Path.Combine(this.testDir, "decrypted.txt");
        const string password = "TestP@ssw0rd!Str0ng";

        var strategy = this.encryptionFactory.Create(algorithm);

        var metadata = await strategy.EncryptFileAsync(
            sourceFile,
            encryptedFile,
            password,
            kdf);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(metadata, Is.Not.Null);
            Assert.That(File.Exists(encryptedFile), Is.True);
        }

        var encryptedBytes = await File.ReadAllBytesAsync(encryptedFile);
        var originalBytes = Encoding.UTF8.GetBytes(originalContent);
        Assert.That(encryptedBytes, Is.Not.EqualTo(originalBytes));

        var decryptResult = await strategy.DecryptFileAsync(
            encryptedFile,
            decryptedFile,
            password,
            kdf,
            metadata);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(decryptResult, Is.True);
            Assert.That(File.Exists(decryptedFile), Is.True);
        }

        var decryptedContent = await File.ReadAllTextAsync(decryptedFile);
        Assert.That(decryptedContent, Is.EqualTo(originalContent));
    }

    [TestCase(EncryptionAlgorithm.Aes, CompressionMode.None)]
    [TestCase(EncryptionAlgorithm.Aes, CompressionMode.Zstd)]
    [TestCase(EncryptionAlgorithm.Aes, CompressionMode.ZstdBest)]
    public async Task EncryptAndDecryptFile_AllCompressionModes_RoundTrip(
        EncryptionAlgorithm algorithm,
        CompressionMode compression)
    {
        var originalContent = string.Concat(Enumerable.Repeat("Compressible data block. ", 200));
        var sourceFile = this.CreateTestFile($"compress-test-{compression}.txt", originalContent);
        var encryptedFile = Path.Combine(this.testDir, $"encrypted-{compression}.bzc");
        var decryptedFile = Path.Combine(this.testDir, $"decrypted-{compression}.txt");
        const string password = "TestP@ssw0rd!Str0ng";

        var strategy = this.encryptionFactory.Create(algorithm);

        var metadata = await strategy.EncryptFileAsync(
            sourceFile,
            encryptedFile,
            password,
            KeyDerivationAlgorithm.PBKDF2,
            compression);
        Assert.That(metadata, Is.Not.Null);

        var decryptResult = await strategy.DecryptFileAsync(
            encryptedFile,
            decryptedFile,
            password,
            KeyDerivationAlgorithm.PBKDF2,
            metadata);
        Assert.That(decryptResult, Is.True);

        var decryptedContent = await File.ReadAllTextAsync(decryptedFile);
        Assert.That(decryptedContent, Is.EqualTo(originalContent));
    }

    [Test]
    public async Task EncryptFile_LargerFile_RoundTrip()
    {
        var largeData = new byte[256 * 1024];
        RandomNumberGenerator.Fill(largeData);
        var sourceFile = Path.Combine(this.testDir, "large.bin");
        await File.WriteAllBytesAsync(sourceFile, largeData);

        var encryptedFile = Path.Combine(this.testDir, "large.bzc");
        var decryptedFile = Path.Combine(this.testDir, "large-decrypted.bin");
        const string password = "TestP@ssw0rd!Str0ng";

        var strategy = this.encryptionFactory.Create(EncryptionAlgorithm.Aes);

        var metadata = await strategy.EncryptFileAsync(
            sourceFile,
            encryptedFile,
            password,
            KeyDerivationAlgorithm.PBKDF2);

        await strategy.DecryptFileAsync(
            encryptedFile,
            decryptedFile,
            password,
            KeyDerivationAlgorithm.PBKDF2,
            metadata);

        var decryptedData = await File.ReadAllBytesAsync(decryptedFile);
        Assert.That(decryptedData, Is.EqualTo(largeData));
    }

    [Test]
    public void EncryptFile_SourceNotFound_ThrowsEncryptionFileNotFoundException()
    {
        var nonExistent = Path.Combine(this.testDir, "nonexistent.txt");
        var dest = Path.Combine(this.testDir, "out.bzc");

        var strategy = this.encryptionFactory.Create(EncryptionAlgorithm.Aes);

        Assert.ThrowsAsync<EncryptionFileNotFoundException>(
            async () =>
                await strategy.EncryptFileAsync(
                    nonExistent,
                    dest,
                    "Password1234!",
                    KeyDerivationAlgorithm.PBKDF2));
    }

    [SetUp]
    public void SetUp()
    {
        ServiceCollection services = [];
        services.AddDomainServices();
        this.provider = services.BuildServiceProvider();
        this.encryptionFactory = this.provider.GetRequiredService<IEncryptionServiceFactory>();

        this.testDir = Path.Combine(Path.GetTempPath(), $"BackupZCrypt-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(this.testDir);
    }

    [TearDown]
    public void TearDown()
    {
        this.provider.Dispose();
        if (Directory.Exists(this.testDir))
        {
            Directory.Delete(this.testDir, true);
        }
    }

    private string CreateTestFile(string name, string content)
    {
        var path = Path.Combine(this.testDir, name);
        File.WriteAllText(path, content);
        return path;
    }
}
