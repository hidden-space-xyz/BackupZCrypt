namespace BackupZCrypt.Test.Integration;

using System.Security.Cryptography;
using System.Text;
using BackupZCrypt.Composition;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Factories.Interfaces;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Encryption;
using Microsoft.Extensions.DependencyInjection;

[TestFixture]
internal sealed class EncryptionRoundTripTests
{
    private IEncryptionServiceFactory encryptionFactory = null!;
    private ServiceProvider provider = null!;
    private string testDir = null!;

    [TestCase(EncryptionAlgorithm.Aes)]
    [TestCase(EncryptionAlgorithm.ChaCha20)]
    public async Task CreateEncryptedFileAndReadBack_RoundTrip(EncryptionAlgorithm algorithm)
    {
        byte[] plaintext = Encoding.UTF8.GetBytes("In-memory plaintext data for testing");
        string encryptedFile = Path.Combine(this.testDir, $"inmem-{algorithm}.bzc");
        const string password = "TestP@ssw0rd!Str0ng";

        IEncryptionAlgorithmStrategy strategy = this.encryptionFactory.Create(algorithm);

        bool result = await strategy.CreateEncryptedFileAsync(
            plaintext,
            encryptedFile,
            password,
            KeyDerivationAlgorithm.PBKDF2);
        Assert.That(result, Is.True);

        byte[] readBack = await strategy.ReadEncryptedFileAsync(
            encryptedFile,
            password,
            KeyDerivationAlgorithm.PBKDF2);

        Assert.That(readBack, Is.EqualTo(plaintext));
    }

    [Test]
    public void DecryptFile_CorruptedFile_ThrowsEncryptionCorruptedFileException()
    {
        string corruptedFile = Path.Combine(this.testDir, "corrupted.bzc");
        File.WriteAllBytes(corruptedFile, [1, 2, 3]);
        string dest = Path.Combine(this.testDir, "out.txt");

        IEncryptionAlgorithmStrategy strategy = this.encryptionFactory.Create(EncryptionAlgorithm.Aes);

        Assert.ThrowsAsync<BackupZCrypt.Domain.Exceptions.EncryptionCorruptedFileException>(
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
        string nonExistent = Path.Combine(this.testDir, "nonexistent.bzc");
        string dest = Path.Combine(this.testDir, "out.txt");

        IEncryptionAlgorithmStrategy strategy = this.encryptionFactory.Create(EncryptionAlgorithm.Aes);

        Assert.ThrowsAsync<BackupZCrypt.Domain.Exceptions.EncryptionFileNotFoundException>(
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
        string sourceFile = this.CreateTestFile("wrong-pass.txt", "secret data");
        string encryptedFile = Path.Combine(this.testDir, "wrong-pass.bzc");
        string decryptedFile = Path.Combine(this.testDir, "wrong-pass-out.txt");

        IEncryptionAlgorithmStrategy strategy = this.encryptionFactory.Create(EncryptionAlgorithm.Aes);

        EncryptionMetadata metadata = await strategy.EncryptFileAsync(
            sourceFile,
            encryptedFile,
            "CorrectPassword1!",
            KeyDerivationAlgorithm.PBKDF2);

        Assert.ThrowsAsync<BackupZCrypt.Domain.Exceptions.EncryptionInvalidPasswordException>(
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
        string sourceFile = this.CreateTestFile("original.txt", originalContent);
        string encryptedFile = Path.Combine(this.testDir, "encrypted.bzc");
        string decryptedFile = Path.Combine(this.testDir, "decrypted.txt");
        const string password = "TestP@ssw0rd!Str0ng";

        IEncryptionAlgorithmStrategy strategy = this.encryptionFactory.Create(algorithm);

        EncryptionMetadata metadata = await strategy.EncryptFileAsync(
            sourceFile,
            encryptedFile,
            password,
            kdf);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(metadata, Is.Not.Null);
            Assert.That(File.Exists(encryptedFile), Is.True);
        }

        byte[] encryptedBytes = await File.ReadAllBytesAsync(encryptedFile);
        byte[] originalBytes = Encoding.UTF8.GetBytes(originalContent);
        Assert.That(encryptedBytes, Is.Not.EqualTo(originalBytes));

        bool decryptResult = await strategy.DecryptFileAsync(
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

        string decryptedContent = await File.ReadAllTextAsync(decryptedFile);
        Assert.That(decryptedContent, Is.EqualTo(originalContent));
    }

    [TestCase(EncryptionAlgorithm.Aes, CompressionMode.None)]
    [TestCase(EncryptionAlgorithm.Aes, CompressionMode.Zstd)]
    [TestCase(EncryptionAlgorithm.Aes, CompressionMode.ZstdBest)]
    public async Task EncryptAndDecryptFile_AllCompressionModes_RoundTrip(
        EncryptionAlgorithm algorithm,
        CompressionMode compression)
    {
        string originalContent = string.Concat(Enumerable.Repeat("Compressible data block. ", 200));
        string sourceFile = this.CreateTestFile($"compress-test-{compression}.txt", originalContent);
        string encryptedFile = Path.Combine(this.testDir, $"encrypted-{compression}.bzc");
        string decryptedFile = Path.Combine(this.testDir, $"decrypted-{compression}.txt");
        const string password = "TestP@ssw0rd!Str0ng";

        IEncryptionAlgorithmStrategy strategy = this.encryptionFactory.Create(algorithm);

        EncryptionMetadata metadata = await strategy.EncryptFileAsync(
            sourceFile,
            encryptedFile,
            password,
            KeyDerivationAlgorithm.PBKDF2,
            compression);
        Assert.That(metadata, Is.Not.Null);

        bool decryptResult = await strategy.DecryptFileAsync(
            encryptedFile,
            decryptedFile,
            password,
            KeyDerivationAlgorithm.PBKDF2,
            metadata);
        Assert.That(decryptResult, Is.True);

        string decryptedContent = await File.ReadAllTextAsync(decryptedFile);
        Assert.That(decryptedContent, Is.EqualTo(originalContent));
    }

    [Test]
    public async Task EncryptFile_LargerFile_RoundTrip()
    {
        byte[] largeData = new byte[256 * 1024];
        RandomNumberGenerator.Fill(largeData);
        string sourceFile = Path.Combine(this.testDir, "large.bin");
        await File.WriteAllBytesAsync(sourceFile, largeData);

        string encryptedFile = Path.Combine(this.testDir, "large.bzc");
        string decryptedFile = Path.Combine(this.testDir, "large-decrypted.bin");
        const string password = "TestP@ssw0rd!Str0ng";

        IEncryptionAlgorithmStrategy strategy = this.encryptionFactory.Create(EncryptionAlgorithm.Aes);

        EncryptionMetadata metadata = await strategy.EncryptFileAsync(
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

        byte[] decryptedData = await File.ReadAllBytesAsync(decryptedFile);
        Assert.That(decryptedData, Is.EqualTo(largeData));
    }

    [Test]
    public void EncryptFile_SourceNotFound_ThrowsEncryptionFileNotFoundException()
    {
        string nonExistent = Path.Combine(this.testDir, "nonexistent.txt");
        string dest = Path.Combine(this.testDir, "out.bzc");

        IEncryptionAlgorithmStrategy strategy = this.encryptionFactory.Create(EncryptionAlgorithm.Aes);

        Assert.ThrowsAsync<BackupZCrypt.Domain.Exceptions.EncryptionFileNotFoundException>(
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
        ServiceCollection services = new();
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
        string path = Path.Combine(this.testDir, name);
        File.WriteAllText(path, content);
        return path;
    }
}
