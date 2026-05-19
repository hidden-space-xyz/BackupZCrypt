namespace BackupZCrypt.Test.Integration;

using BackupZCrypt.Application.Orchestrators.Interfaces;
using BackupZCrypt.Composition;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.ValueObjects.Backup;
using Microsoft.Extensions.DependencyInjection;

[TestFixture]
internal sealed class OrchestratorIntegrationTests
{
    private ServiceProvider provider = null!;
    private IBackupOrchestrator orchestrator = null!;
    private string testDir = null!;
    private string sourceDir = null!;
    private string destDir = null!;

    [SetUp]
    public void SetUp()
    {
        ServiceCollection services = [];
        services.AddDomainServices();
        services.AddApplicationServices();
        this.provider = services.BuildServiceProvider();
        this.orchestrator = this.provider.GetRequiredService<IBackupOrchestrator>();

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
        var sourceFile = Path.Combine(this.sourceDir, "test.txt");
        await File.WriteAllTextAsync(sourceFile, originalContent);

        var backupDir = Path.Combine(this.testDir, "backup");
        const string password = "IntegrationP@ss1";

        BackupRequest encryptRequest = new(
            sourceFile,
            backupDir,
            password,
            password,
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.PBKDF2,
            BackupOperation.Create,
            ProceedOnWarnings: true);

        Progress<BackupStatus> progress = new();
        var encryptResult = await orchestrator.ExecuteAsync(
            encryptRequest,
            progress);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(encryptResult.IsSuccess, Is.True);
            Assert.That(encryptResult.Value.IsSuccess, Is.True);
            Assert.That(File.Exists(Path.Combine(backupDir, "manifest.bzc")), Is.True);
            Assert.That(Directory.Exists(Path.Combine(backupDir, "chunks")), Is.True);
        }

        var decryptedDir = Path.Combine(this.testDir, "decrypted");

        BackupRequest decryptRequest = new(
            backupDir,
            decryptedDir,
            password,
            password,
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.PBKDF2,
            BackupOperation.Restore,
            ProceedOnWarnings: true);

        var decryptResult = await orchestrator.ExecuteAsync(
            decryptRequest,
            progress);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(decryptResult.IsSuccess, Is.True);
            Assert.That(decryptResult.Value.IsSuccess, Is.True);
        }

        var decryptedContent = await File.ReadAllTextAsync(
            Path.Combine(decryptedDir, "test.txt"));
        Assert.That(decryptedContent, Is.EqualTo(originalContent));
    }

    [Test]
    public async Task ExecuteAsync_ValidationErrors_EmptyPassword_ReturnsErrors()
    {
        var sourceFile = Path.Combine(this.sourceDir, "test.txt");
        await File.WriteAllTextAsync(sourceFile, "content");
        var destFile = Path.Combine(this.destDir, "test.bzc");

        BackupRequest request = new(
            sourceFile,
            destFile,
            string.Empty,
            string.Empty,
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.PBKDF2,
            BackupOperation.Create);

        Progress<BackupStatus> progress = new();
        var result = await orchestrator.ExecuteAsync(request, progress);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.HasErrors, Is.True);
        }
    }

    [Test]
    public async Task ExecuteAsync_ValidationErrors_PasswordMismatch_ReturnsErrors()
    {
        var sourceFile = Path.Combine(this.sourceDir, "test.txt");
        await File.WriteAllTextAsync(sourceFile, "content");
        var destFile = Path.Combine(this.destDir, "test.bzc");

        BackupRequest request = new(
            sourceFile,
            destFile,
            "Password1!",
            "DifferentPass1!",
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.PBKDF2,
            BackupOperation.Create);

        Progress<BackupStatus> progress = new();
        var result = await orchestrator.ExecuteAsync(request, progress);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.HasErrors, Is.True);
        }
    }

    [Test]
    public async Task ExecuteAsync_SourceNotFound_ReturnsErrors()
    {
        var nonExistent = Path.Combine(this.sourceDir, "nonexistent.txt");
        var destFile = Path.Combine(this.destDir, "out.bzc");

        BackupRequest request = new(
            nonExistent,
            destFile,
            "Password1!",
            "Password1!",
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.PBKDF2,
            BackupOperation.Create);

        Progress<BackupStatus> progress = new();
        var result = await orchestrator.ExecuteAsync(request, progress);

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

        var encryptedDir = Path.Combine(this.testDir, "encrypted");
        const string password = "IntegrationP@ss1";

        BackupRequest request = new(
            this.sourceDir,
            encryptedDir,
            password,
            password,
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.PBKDF2,
            BackupOperation.Create,
            ProceedOnWarnings: true);

        Progress<BackupStatus> progress = new();
        var result = await orchestrator.ExecuteAsync(request, progress);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.ProcessedFiles, Is.EqualTo(2));
            Assert.That(result.Value.TotalFiles, Is.EqualTo(2));
        }
    }

    [Test]
    public async Task ExecuteAsync_Directory_EncryptAndDecrypt_RoundTrip()
    {
        var content1 = string.Concat(Enumerable.Repeat("File one content! ", 50));
        var content2 = string.Concat(Enumerable.Repeat("File two content! ", 50));
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "doc1.txt"), content1);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "doc2.txt"), content2);

        var encryptedDir = Path.Combine(this.testDir, "encrypted");
        var decryptedDir = Path.Combine(this.testDir, "decrypted");
        const string password = "IntegrationP@ss1";

        BackupRequest encryptRequest = new(
            this.sourceDir,
            encryptedDir,
            password,
            password,
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.PBKDF2,
            BackupOperation.Create,
            CompressionMode.Zstd,
            ProceedOnWarnings: true);

        Progress<BackupStatus> progress = new();
        var encryptResult = await orchestrator.ExecuteAsync(
            encryptRequest, progress);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(encryptResult.IsSuccess, Is.True);
            Assert.That(encryptResult.Value.IsSuccess, Is.True);
            Assert.That(File.Exists(Path.Combine(encryptedDir, "manifest.bzc")), Is.True);
        }

        BackupRequest decryptRequest = new(
            encryptedDir,
            decryptedDir,
            password,
            password,
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.PBKDF2,
            BackupOperation.Restore,
            ProceedOnWarnings: true);

        var decryptResult = await orchestrator.ExecuteAsync(
            decryptRequest, progress);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(decryptResult.IsSuccess, Is.True);
            Assert.That(decryptResult.Value.IsSuccess, Is.True);
            Assert.That(decryptResult.Value.ProcessedFiles, Is.EqualTo(2));
        }

        var decrypted1 = await File.ReadAllTextAsync(Path.Combine(decryptedDir, "doc1.txt"));
        var decrypted2 = await File.ReadAllTextAsync(Path.Combine(decryptedDir, "doc2.txt"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(decrypted1, Is.EqualTo(content1));
            Assert.That(decrypted2, Is.EqualTo(content2));
        }
    }

    [Test]
    public async Task ExecuteAsync_Directory_DecryptWithoutManifest_Fails()
    {
        var noManifestDir = Path.Combine(this.testDir, "no-manifest");
        Directory.CreateDirectory(noManifestDir);
        await File.WriteAllTextAsync(Path.Combine(noManifestDir, "file.bzc"), "fake");

        BackupRequest decryptRequest = new(
            noManifestDir,
            this.destDir,
            "IntegrationP@ss1",
            "IntegrationP@ss1",
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.PBKDF2,
            BackupOperation.Restore,
            ProceedOnWarnings: true);

        Progress<BackupStatus> progress = new();
        var result = await orchestrator.ExecuteAsync(
            decryptRequest, progress);

        Assert.That(result.IsSuccess, Is.False);
    }

    [Test]
    public async Task ExecuteAsync_Directory_Update_AddModifyDelete_RoundTrip()
    {
        const string content1 = "Original file one content";
        const string content2 = "Original file two content";
        const string content3 = "Original file three content";
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "file1.txt"), content1);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "file2.txt"), content2);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "file3.txt"), content3);

        var encryptedDir = Path.Combine(this.testDir, "encrypted-upd");
        const string password = "IntegrationP@ss1";

        BackupRequest encryptRequest = new(
            this.sourceDir,
            encryptedDir,
            password,
            password,
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.PBKDF2,
            BackupOperation.Create,
            CompressionMode.Zstd,
            ProceedOnWarnings: true);

        Progress<BackupStatus> progress = new();
        var encryptResult = await orchestrator.ExecuteAsync(
            encryptRequest, progress);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(encryptResult.IsSuccess, Is.True);
            Assert.That(encryptResult.Value.IsSuccess, Is.True);
            Assert.That(encryptResult.Value.ProcessedFiles, Is.EqualTo(3));
        }

        // Modify source: change file1, delete file2, add file4
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "file1.txt"), "Modified file one content");
        File.Delete(Path.Combine(sourceDir, "file2.txt"));
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "file4.txt"), "New file four content");

        BackupRequest updateRequest = new(
            this.sourceDir,
            encryptedDir,
            password,
            password,
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.PBKDF2,
            BackupOperation.Update,
            ProceedOnWarnings: true);

        var updateResult = await orchestrator.ExecuteAsync(
            updateRequest, progress);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(updateResult.IsSuccess, Is.True);
            Assert.That(updateResult.Value.IsSuccess, Is.True);
            // file1 modified + file4 new = 2 processed; file3 unchanged
            Assert.That(updateResult.Value.ProcessedFiles, Is.EqualTo(2));
        }

        // file2.bzc should be deleted from backup
        Assert.That(File.Exists(Path.Combine(encryptedDir, "file2.txt.bzc")), Is.False);

        // Decrypt and verify content matches updated source
        var decryptedDir = Path.Combine(this.testDir, "decrypted-upd");

        BackupRequest decryptRequest = new(
            encryptedDir,
            decryptedDir,
            password,
            password,
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.PBKDF2,
            BackupOperation.Restore,
            ProceedOnWarnings: true);

        var decryptResult = await orchestrator.ExecuteAsync(
            decryptRequest, progress);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(decryptResult.IsSuccess, Is.True);
            Assert.That(decryptResult.Value.IsSuccess, Is.True);
            Assert.That(decryptResult.Value.ProcessedFiles, Is.EqualTo(3));
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                await File.ReadAllTextAsync(Path.Combine(decryptedDir, "file1.txt")),
                Is.EqualTo("Modified file one content"));
            Assert.That(
                await File.ReadAllTextAsync(Path.Combine(decryptedDir, "file3.txt")),
                Is.EqualTo(content3));
            Assert.That(
                await File.ReadAllTextAsync(Path.Combine(decryptedDir, "file4.txt")),
                Is.EqualTo("New file four content"));
            Assert.That(File.Exists(Path.Combine(decryptedDir, "file2.txt")), Is.False);
        }
    }

    [Test]
    public async Task ExecuteAsync_Directory_Update_NoChanges_ProcessesZeroFiles()
    {
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "stable.txt"), "Stable content");

        var encryptedDir = Path.Combine(this.testDir, "encrypted-nc");
        const string password = "IntegrationP@ss1";

        BackupRequest encryptRequest = new(
            this.sourceDir,
            encryptedDir,
            password,
            password,
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.PBKDF2,
            BackupOperation.Create,
            ProceedOnWarnings: true);

        Progress<BackupStatus> progress = new();
        await orchestrator.ExecuteAsync(encryptRequest, progress);

        BackupRequest updateRequest = new(
            this.sourceDir,
            encryptedDir,
            password,
            password,
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.PBKDF2,
            BackupOperation.Update,
            ProceedOnWarnings: true);

        var updateResult = await orchestrator.ExecuteAsync(
            updateRequest, progress);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(updateResult.IsSuccess, Is.True);
            Assert.That(updateResult.Value.IsSuccess, Is.True);
            Assert.That(updateResult.Value.ProcessedFiles, Is.Zero);
            Assert.That(updateResult.Value.TotalFiles, Is.Zero);
        }
    }

    [Test]
    public async Task ExecuteAsync_Directory_ChaCha20Argon2id_RoundTrip()
    {
        var content1 = "ChaCha20+Argon2id test content one";
        var content2 = "ChaCha20+Argon2id test content two";
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "a.txt"), content1);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "b.txt"), content2);

        var encryptedDir = Path.Combine(this.testDir, "encrypted-chacha");
        var decryptedDir = Path.Combine(this.testDir, "decrypted-chacha");
        const string password = "IntegrationP@ss1";

        BackupRequest encryptRequest = new(
            this.sourceDir,
            encryptedDir,
            password,
            password,
            EncryptionAlgorithm.ChaCha20,
            KeyDerivationAlgorithm.Argon2id,
            BackupOperation.Create,
            CompressionMode.ZstdBest,
            ProceedOnWarnings: true);

        Progress<BackupStatus> progress = new();
        var encryptResult = await orchestrator.ExecuteAsync(
            encryptRequest, progress);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(encryptResult.IsSuccess, Is.True);
            Assert.That(encryptResult.Value.IsSuccess, Is.True);
            Assert.That(encryptResult.Value.ProcessedFiles, Is.EqualTo(2));
        }

        BackupRequest decryptRequest = new(
            encryptedDir,
            decryptedDir,
            password,
            password,
            EncryptionAlgorithm.ChaCha20,
            KeyDerivationAlgorithm.Argon2id,
            BackupOperation.Restore,
            ProceedOnWarnings: true);

        var decryptResult = await orchestrator.ExecuteAsync(
            decryptRequest, progress);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(decryptResult.IsSuccess, Is.True);
            Assert.That(decryptResult.Value.IsSuccess, Is.True);
            Assert.That(decryptResult.Value.ProcessedFiles, Is.EqualTo(2));
        }

        var decrypted1 = await File.ReadAllTextAsync(Path.Combine(decryptedDir, "a.txt"));
        var decrypted2 = await File.ReadAllTextAsync(Path.Combine(decryptedDir, "b.txt"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(decrypted1, Is.EqualTo(content1));
            Assert.That(decrypted2, Is.EqualTo(content2));
        }
    }
}
