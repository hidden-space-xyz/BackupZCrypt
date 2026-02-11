using System.Text;
using CloudZCrypt.Application.Orchestrators.Interfaces;
using CloudZCrypt.Application.ValueObjects;
using CloudZCrypt.Composition;
using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.ValueObjects.FileCrypt;
using Microsoft.Extensions.DependencyInjection;

namespace CloudZCrypt.Test.Integration;

[TestFixture]
internal sealed class OrchestratorIntegrationTests
{
    private ServiceProvider _provider = null!;
    private IFileCryptOrchestrator _orchestrator = null!;
    private string _testDir = null!;
    private string _sourceDir = null!;
    private string _destDir = null!;

    [SetUp]
    public void SetUp()
    {
        ServiceCollection services = new();
        services.AddDomainServices();
        services.AddApplicationServices();
        _provider = services.BuildServiceProvider();
        _orchestrator = _provider.GetRequiredService<IFileCryptOrchestrator>();

        _testDir = Path.Combine(Path.GetTempPath(), $"cloudzcrypt-orch-{Guid.NewGuid():N}");
        _sourceDir = Path.Combine(_testDir, "source");
        _destDir = Path.Combine(_testDir, "dest");
        Directory.CreateDirectory(_sourceDir);
        Directory.CreateDirectory(_destDir);
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

    [Test]
    public async Task ExecuteAsync_SingleFile_EncryptAndDecrypt_RoundTrip()
    {
        string originalContent = "Integration test file content!";
        string sourceFile = Path.Combine(_sourceDir, "test.txt");
        File.WriteAllText(sourceFile, originalContent);

        string encryptedFile = Path.Combine(_destDir, "test.czc");
        string password = "IntegrationP@ss1";

        FileCryptRequest encryptRequest = new(
            sourceFile, encryptedFile, password, password,
            EncryptionAlgorithm.Aes, KeyDerivationAlgorithm.PBKDF2,
            EncryptOperation.Encrypt, NameObfuscationMode.None,
            ProceedOnWarnings: true
        );

        Progress<FileCryptStatus> progress = new();
        Result<FileCryptResult> encryptResult = await _orchestrator.ExecuteAsync(
            encryptRequest, progress);

        Assert.That(encryptResult.IsSuccess, Is.True);
        Assert.That(encryptResult.Value.IsSuccess, Is.True);
        Assert.That(File.Exists(encryptedFile), Is.True);

        string decryptedDir = Path.Combine(_testDir, "decrypted");
        Directory.CreateDirectory(decryptedDir);
        string decryptedFile = Path.Combine(decryptedDir, "test.txt");

        FileCryptRequest decryptRequest = new(
            encryptedFile, decryptedFile, password, password,
            EncryptionAlgorithm.Aes, KeyDerivationAlgorithm.PBKDF2,
            EncryptOperation.Decrypt, NameObfuscationMode.None,
            ProceedOnWarnings: true
        );

        Result<FileCryptResult> decryptResult = await _orchestrator.ExecuteAsync(
            decryptRequest, progress);

        Assert.That(decryptResult.IsSuccess, Is.True);
        Assert.That(decryptResult.Value.IsSuccess, Is.True);

        string decryptedContent = await File.ReadAllTextAsync(decryptedFile);
        Assert.That(decryptedContent, Is.EqualTo(originalContent));
    }

    [Test]
    public async Task ExecuteAsync_ValidationErrors_EmptyPassword_ReturnsErrors()
    {
        string sourceFile = Path.Combine(_sourceDir, "test.txt");
        File.WriteAllText(sourceFile, "content");
        string destFile = Path.Combine(_destDir, "test.czc");

        FileCryptRequest request = new(
            sourceFile, destFile, "", "",
            EncryptionAlgorithm.Aes, KeyDerivationAlgorithm.PBKDF2,
            EncryptOperation.Encrypt, NameObfuscationMode.None
        );

        Progress<FileCryptStatus> progress = new();
        Result<FileCryptResult> result = await _orchestrator.ExecuteAsync(request, progress);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.HasErrors, Is.True);
    }

    [Test]
    public async Task ExecuteAsync_ValidationErrors_PasswordMismatch_ReturnsErrors()
    {
        string sourceFile = Path.Combine(_sourceDir, "test.txt");
        File.WriteAllText(sourceFile, "content");
        string destFile = Path.Combine(_destDir, "test.czc");

        FileCryptRequest request = new(
            sourceFile, destFile, "Password1!", "DifferentPass1!",
            EncryptionAlgorithm.Aes, KeyDerivationAlgorithm.PBKDF2,
            EncryptOperation.Encrypt, NameObfuscationMode.None
        );

        Progress<FileCryptStatus> progress = new();
        Result<FileCryptResult> result = await _orchestrator.ExecuteAsync(request, progress);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.HasErrors, Is.True);
    }

    [Test]
    public async Task ExecuteAsync_SourceNotFound_ReturnsErrors()
    {
        string nonExistent = Path.Combine(_sourceDir, "nonexistent.txt");
        string destFile = Path.Combine(_destDir, "out.czc");

        FileCryptRequest request = new(
            nonExistent, destFile, "Password1!", "Password1!",
            EncryptionAlgorithm.Aes, KeyDerivationAlgorithm.PBKDF2,
            EncryptOperation.Encrypt, NameObfuscationMode.None
        );

        Progress<FileCryptStatus> progress = new();
        Result<FileCryptResult> result = await _orchestrator.ExecuteAsync(request, progress);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.HasErrors, Is.True);
    }

    [Test]
    public async Task ExecuteAsync_Directory_EncryptMultipleFiles()
    {
        File.WriteAllText(Path.Combine(_sourceDir, "file1.txt"), "Content 1");
        File.WriteAllText(Path.Combine(_sourceDir, "file2.txt"), "Content 2");

        string encryptedDir = Path.Combine(_testDir, "encrypted");
        string password = "IntegrationP@ss1";

        FileCryptRequest request = new(
            _sourceDir, encryptedDir, password, password,
            EncryptionAlgorithm.Aes, KeyDerivationAlgorithm.PBKDF2,
            EncryptOperation.Encrypt, NameObfuscationMode.None,
            ProceedOnWarnings: true
        );

        Progress<FileCryptStatus> progress = new();
        Result<FileCryptResult> result = await _orchestrator.ExecuteAsync(request, progress);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.ProcessedFiles, Is.EqualTo(2));
        Assert.That(result.Value.TotalFiles, Is.EqualTo(2));
    }
}
