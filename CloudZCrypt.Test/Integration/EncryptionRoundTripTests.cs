using System.Text;
using CloudZCrypt.Composition;
using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Factories.Interfaces;
using CloudZCrypt.Domain.Strategies.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace CloudZCrypt.Test.Integration;

[TestFixture]
internal sealed class EncryptionRoundTripTests
{
    private ServiceProvider _provider = null!;
    private IEncryptionServiceFactory _encryptionFactory = null!;
    private string _testDir = null!;

    [SetUp]
    public void SetUp()
    {
        ServiceCollection services = new();
        services.AddDomainServices();
        _provider = services.BuildServiceProvider();
        _encryptionFactory = _provider.GetRequiredService<IEncryptionServiceFactory>();

        _testDir = Path.Combine(Path.GetTempPath(), $"cloudzcrypt-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    [TearDown]
    public void TearDown()
    {
        _provider.Dispose();
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, true);
        }
    }

    private string CreateTestFile(string name, string content)
    {
        string path = Path.Combine(_testDir, name);
        File.WriteAllText(path, content);
        return path;
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
        KeyDerivationAlgorithm kdf
    )
    {
        string originalContent = "This is a test file for encryption round trip!";
        string sourceFile = CreateTestFile("original.txt", originalContent);
        string encryptedFile = Path.Combine(_testDir, "encrypted.czc");
        string decryptedFile = Path.Combine(_testDir, "decrypted.txt");
        string password = "TestP@ssw0rd!Str0ng";

        IEncryptionAlgorithmStrategy strategy = _encryptionFactory.Create(algorithm);

        bool encryptResult = await strategy.EncryptFileAsync(
            sourceFile,
            encryptedFile,
            password,
            kdf
        );
        Assert.That(encryptResult, Is.True);
        Assert.That(File.Exists(encryptedFile), Is.True);

        byte[] encryptedBytes = await File.ReadAllBytesAsync(encryptedFile);
        byte[] originalBytes = Encoding.UTF8.GetBytes(originalContent);
        Assert.That(encryptedBytes, Is.Not.EqualTo(originalBytes));

        bool decryptResult = await strategy.DecryptFileAsync(
            encryptedFile,
            decryptedFile,
            password,
            kdf
        );
        Assert.That(decryptResult, Is.True);
        Assert.That(File.Exists(decryptedFile), Is.True);

        string decryptedContent = await File.ReadAllTextAsync(decryptedFile);
        Assert.That(decryptedContent, Is.EqualTo(originalContent));
    }

    [TestCase(EncryptionAlgorithm.Aes, CompressionMode.None)]
    [TestCase(EncryptionAlgorithm.Aes, CompressionMode.GZip)]
    [TestCase(EncryptionAlgorithm.Aes, CompressionMode.BZip2)]
    public async Task EncryptAndDecryptFile_AllCompressionModes_RoundTrip(
        EncryptionAlgorithm algorithm,
        CompressionMode compression
    )
    {
        string originalContent = string.Concat(Enumerable.Repeat("Compressible data block. ", 200));
        string sourceFile = CreateTestFile($"compress-test-{compression}.txt", originalContent);
        string encryptedFile = Path.Combine(_testDir, $"encrypted-{compression}.czc");
        string decryptedFile = Path.Combine(_testDir, $"decrypted-{compression}.txt");
        string password = "TestP@ssw0rd!Str0ng";

        IEncryptionAlgorithmStrategy strategy = _encryptionFactory.Create(algorithm);

        bool encryptResult = await strategy.EncryptFileAsync(
            sourceFile,
            encryptedFile,
            password,
            KeyDerivationAlgorithm.PBKDF2,
            compression
        );
        Assert.That(encryptResult, Is.True);

        bool decryptResult = await strategy.DecryptFileAsync(
            encryptedFile,
            decryptedFile,
            password,
            KeyDerivationAlgorithm.PBKDF2
        );
        Assert.That(decryptResult, Is.True);

        string decryptedContent = await File.ReadAllTextAsync(decryptedFile);
        Assert.That(decryptedContent, Is.EqualTo(originalContent));
    }

    [Test]
    public async Task DecryptFile_WrongPassword_ThrowsEncryptionInvalidPasswordException()
    {
        string sourceFile = CreateTestFile("wrong-pass.txt", "secret data");
        string encryptedFile = Path.Combine(_testDir, "wrong-pass.czc");
        string decryptedFile = Path.Combine(_testDir, "wrong-pass-out.txt");

        IEncryptionAlgorithmStrategy strategy = _encryptionFactory.Create(EncryptionAlgorithm.Aes);

        await strategy.EncryptFileAsync(
            sourceFile,
            encryptedFile,
            "CorrectPassword1!",
            KeyDerivationAlgorithm.PBKDF2
        );

        Assert.ThrowsAsync<CloudZCrypt.Domain.Exceptions.EncryptionInvalidPasswordException>(
            async () =>
                await strategy.DecryptFileAsync(
                    encryptedFile,
                    decryptedFile,
                    "WrongPassword1!!",
                    KeyDerivationAlgorithm.PBKDF2
                )
        );
    }

    [Test]
    public void EncryptFile_SourceNotFound_ThrowsEncryptionFileNotFoundException()
    {
        string nonExistent = Path.Combine(_testDir, "nonexistent.txt");
        string dest = Path.Combine(_testDir, "out.czc");

        IEncryptionAlgorithmStrategy strategy = _encryptionFactory.Create(EncryptionAlgorithm.Aes);

        Assert.ThrowsAsync<CloudZCrypt.Domain.Exceptions.EncryptionFileNotFoundException>(
            async () =>
                await strategy.EncryptFileAsync(
                    nonExistent,
                    dest,
                    "Password1234!",
                    KeyDerivationAlgorithm.PBKDF2
                )
        );
    }

    [Test]
    public void DecryptFile_SourceNotFound_ThrowsEncryptionFileNotFoundException()
    {
        string nonExistent = Path.Combine(_testDir, "nonexistent.czc");
        string dest = Path.Combine(_testDir, "out.txt");

        IEncryptionAlgorithmStrategy strategy = _encryptionFactory.Create(EncryptionAlgorithm.Aes);

        Assert.ThrowsAsync<CloudZCrypt.Domain.Exceptions.EncryptionFileNotFoundException>(
            async () =>
                await strategy.DecryptFileAsync(
                    nonExistent,
                    dest,
                    "Password1234!",
                    KeyDerivationAlgorithm.PBKDF2
                )
        );
    }

    [Test]
    public void DecryptFile_CorruptedFile_ThrowsEncryptionCorruptedFileException()
    {
        string corruptedFile = Path.Combine(_testDir, "corrupted.czc");
        File.WriteAllBytes(corruptedFile, [1, 2, 3]);
        string dest = Path.Combine(_testDir, "out.txt");

        IEncryptionAlgorithmStrategy strategy = _encryptionFactory.Create(EncryptionAlgorithm.Aes);

        Assert.ThrowsAsync<CloudZCrypt.Domain.Exceptions.EncryptionCorruptedFileException>(
            async () =>
                await strategy.DecryptFileAsync(
                    corruptedFile,
                    dest,
                    "Password1234!",
                    KeyDerivationAlgorithm.PBKDF2
                )
        );
    }

    [TestCase(EncryptionAlgorithm.Aes)]
    [TestCase(EncryptionAlgorithm.ChaCha20)]
    public async Task CreateEncryptedFileAndReadBack_RoundTrip(EncryptionAlgorithm algorithm)
    {
        byte[] plaintext = Encoding.UTF8.GetBytes("In-memory plaintext data for testing");
        string encryptedFile = Path.Combine(_testDir, $"inmem-{algorithm}.czc");
        string password = "TestP@ssw0rd!Str0ng";

        IEncryptionAlgorithmStrategy strategy = _encryptionFactory.Create(algorithm);

        bool result = await strategy.CreateEncryptedFileAsync(
            plaintext,
            encryptedFile,
            password,
            KeyDerivationAlgorithm.PBKDF2
        );
        Assert.That(result, Is.True);

        byte[] readBack = await strategy.ReadEncryptedFileAsync(
            encryptedFile,
            password,
            KeyDerivationAlgorithm.PBKDF2
        );

        Assert.That(readBack, Is.EqualTo(plaintext));
    }

    [Test]
    public async Task EncryptFile_LargerFile_RoundTrip()
    {
        byte[] largeData = new byte[256 * 1024];
        Random.Shared.NextBytes(largeData);
        string sourceFile = Path.Combine(_testDir, "large.bin");
        await File.WriteAllBytesAsync(sourceFile, largeData);

        string encryptedFile = Path.Combine(_testDir, "large.czc");
        string decryptedFile = Path.Combine(_testDir, "large-decrypted.bin");
        string password = "TestP@ssw0rd!Str0ng";

        IEncryptionAlgorithmStrategy strategy = _encryptionFactory.Create(EncryptionAlgorithm.Aes);

        await strategy.EncryptFileAsync(
            sourceFile,
            encryptedFile,
            password,
            KeyDerivationAlgorithm.PBKDF2
        );

        await strategy.DecryptFileAsync(
            encryptedFile,
            decryptedFile,
            password,
            KeyDerivationAlgorithm.PBKDF2
        );

        byte[] decryptedData = await File.ReadAllBytesAsync(decryptedFile);
        Assert.That(decryptedData, Is.EqualTo(largeData));
    }
}
