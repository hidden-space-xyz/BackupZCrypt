namespace BackupZCrypt.Test.Integration;

using System.Text;
using BackupZCrypt.Application.Orchestrators.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Composition;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.ValueObjects.FileCrypt;
using Microsoft.Extensions.DependencyInjection;

[TestFixture]
internal sealed class OrchestratorIntegrationTests
{
    private ServiceProvider provider = null!;
    private IFileCryptOrchestrator orchestrator = null!;
    private string testDir = null!;
    private string sourceDir = null!;
    private string destDir = null!;

    [SetUp]
    public void SetUp()
    {
        ServiceCollection services = new();
        services.AddDomainServices();
        services.AddApplicationServices();
        this.provider = services.BuildServiceProvider();
        this.orchestrator = this.provider.GetRequiredService<IFileCryptOrchestrator>();

        this.testDir = Path.Combine(Path.GetTempPath(), $"BackupZCrypt-orch-{Guid.NewGuid():N}");
        this.sourceDir = Path.Combine(this.testDir, "source");
        this.destDir = Path.Combine(this.testDir, "dest");
        Directory.CreateDirectory(this.sourceDir);
        Directory.CreateDirectory(this.destDir);
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

    [Test]
    public async Task ExecuteAsync_SingleFile_EncryptAndDecrypt_RoundTrip()
    {
        const string originalContent = "Integration test file content!";
        string sourceFile = Path.Combine(this.sourceDir, "test.txt");
        await File.WriteAllTextAsync(sourceFile, originalContent);

        string encryptedFile = Path.Combine(this.destDir, "test.czc");
        const string password = "IntegrationP@ss1";

        FileCryptRequest encryptRequest = new(
            sourceFile,
            encryptedFile,
            password,
            password,
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.PBKDF2,
            EncryptOperation.Encrypt,
            NameObfuscationMode.None,
            ProceedOnWarnings: true);

        Progress<FileCryptStatus> progress = new();
        Result<FileCryptResult> encryptResult = await orchestrator.ExecuteAsync(
            encryptRequest,
            progress);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(encryptResult.IsSuccess, Is.True);
            Assert.That(encryptResult.Value.IsSuccess, Is.True);
            Assert.That(File.Exists(encryptedFile), Is.True);
        }

        string decryptedDir = Path.Combine(this.testDir, "decrypted");
        Directory.CreateDirectory(decryptedDir);
        string decryptedFile = Path.Combine(decryptedDir, "test.txt");

        FileCryptRequest decryptRequest = new(
            encryptedFile,
            decryptedFile,
            password,
            password,
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.PBKDF2,
            EncryptOperation.Decrypt,
            NameObfuscationMode.None,
            ProceedOnWarnings: true);

        Result<FileCryptResult> decryptResult = await orchestrator.ExecuteAsync(
            decryptRequest,
            progress);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(decryptResult.IsSuccess, Is.True);
            Assert.That(decryptResult.Value.IsSuccess, Is.True);
        }

        string decryptedContent = await File.ReadAllTextAsync(decryptedFile);
        Assert.That(decryptedContent, Is.EqualTo(originalContent));
    }

    [Test]
    public async Task ExecuteAsync_ValidationErrors_EmptyPassword_ReturnsErrors()
    {
        string sourceFile = Path.Combine(this.sourceDir, "test.txt");
        await File.WriteAllTextAsync(sourceFile, "content");
        string destFile = Path.Combine(this.destDir, "test.czc");

        FileCryptRequest request = new(
            sourceFile,
            destFile,
            string.Empty,
            string.Empty,
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.PBKDF2,
            EncryptOperation.Encrypt,
            NameObfuscationMode.None);

        Progress<FileCryptStatus> progress = new();
        Result<FileCryptResult> result = await orchestrator.ExecuteAsync(request, progress);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.HasErrors, Is.True);
        }
    }

    [Test]
    public async Task ExecuteAsync_ValidationErrors_PasswordMismatch_ReturnsErrors()
    {
        string sourceFile = Path.Combine(this.sourceDir, "test.txt");
        await File.WriteAllTextAsync(sourceFile, "content");
        string destFile = Path.Combine(this.destDir, "test.czc");

        FileCryptRequest request = new(
            sourceFile,
            destFile,
            "Password1!",
            "DifferentPass1!",
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.PBKDF2,
            EncryptOperation.Encrypt,
            NameObfuscationMode.None);

        Progress<FileCryptStatus> progress = new();
        Result<FileCryptResult> result = await orchestrator.ExecuteAsync(request, progress);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.HasErrors, Is.True);
        }
    }

    [Test]
    public async Task ExecuteAsync_SourceNotFound_ReturnsErrors()
    {
        string nonExistent = Path.Combine(this.sourceDir, "nonexistent.txt");
        string destFile = Path.Combine(this.destDir, "out.czc");

        FileCryptRequest request = new(
            nonExistent,
            destFile,
            "Password1!",
            "Password1!",
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.PBKDF2,
            EncryptOperation.Encrypt,
            NameObfuscationMode.None);

        Progress<FileCryptStatus> progress = new();
        Result<FileCryptResult> result = await orchestrator.ExecuteAsync(request, progress);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.HasErrors, Is.True);
        }
    }

    [Test]
    public async Task ExecuteAsync_Directory_EncryptMultipleFiles()
    {
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "file1.txt"), "Content 1");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "file2.txt"), "Content 2");

        string encryptedDir = Path.Combine(this.testDir, "encrypted");
        const string password = "IntegrationP@ss1";

        FileCryptRequest request = new(
            this.sourceDir,
            encryptedDir,
            password,
            password,
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.PBKDF2,
            EncryptOperation.Encrypt,
            NameObfuscationMode.None,
            ProceedOnWarnings: true);

        Progress<FileCryptStatus> progress = new();
        Result<FileCryptResult> result = await orchestrator.ExecuteAsync(request, progress);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.ProcessedFiles, Is.EqualTo(2));
            Assert.That(result.Value.TotalFiles, Is.EqualTo(2));
        }
    }
}
