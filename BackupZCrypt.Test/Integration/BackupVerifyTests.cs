using BackupZCrypt.Application.Orchestrators.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.ValueObjects.Backup;
using BackupZCrypt.Domain.ValueObjects.Localization;
using BackupZCrypt.Test.Common;

using Microsoft.Extensions.DependencyInjection;

namespace BackupZCrypt.Test.Integration;

public sealed class BackupVerifyTests
{
    private const string Password = "Correct-Horse-Battery-Staple-42";

    [TestCase(CompressionMode.None)]
    [TestCase(CompressionMode.Zstd)]
    public async Task Verify_IntactBackup_Succeeds(CompressionMode compression)
    {
        await using var provider = TestHost.CreateProvider();
        var orchestrator = provider.GetRequiredService<IBackupOrchestrator>();

        using var source = new TempDir();
        using var destination = new TempDir();
        await CreateBackupAsync(orchestrator, source, destination, compression);

        var result = await orchestrator.ExecuteAsync(
            NewRequest(destination.Path, BackupOperation.Verify, compression),
            new RecordingProgress<BackupStatus>()
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.IsSuccess, Is.True);
            Assert.That(result.Value.TotalFiles, Is.EqualTo(3));
            Assert.That(result.Value.ProcessedFiles, Is.EqualTo(result.Value.TotalFiles));
            Assert.That(result.Value.Errors, Is.Empty);
        }
    }

    [Test]
    public async Task Verify_WrongPassword_FailsWithInvalidPassword()
    {
        await using var provider = TestHost.CreateProvider();
        var orchestrator = provider.GetRequiredService<IBackupOrchestrator>();

        using var source = new TempDir();
        using var destination = new TempDir();
        await CreateBackupAsync(orchestrator, source, destination, CompressionMode.None);

        var result = await orchestrator.ExecuteAsync(
            NewRequest(destination.Path, BackupOperation.Verify, password: "a-different-password-1"),
            new RecordingProgress<BackupStatus>()
        );

        Assert.That(CollectCodes(result), Does.Contain(MessageCode.VerifyInvalidPassword));
    }

    [Test]
    public async Task Verify_CorruptedChunk_ReportsIntegrityErrorForAffectedFile()
    {
        await using var provider = TestHost.CreateProvider();
        var orchestrator = provider.GetRequiredService<IBackupOrchestrator>();

        using var source = new TempDir();
        using var destination = new TempDir();
        await CreateBackupAsync(orchestrator, source, destination, CompressionMode.None);

        CorruptOneChunk(destination.Path);

        var result = await orchestrator.ExecuteAsync(
            NewRequest(destination.Path, BackupOperation.Verify),
            new RecordingProgress<BackupStatus>()
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.IsSuccess, Is.False);
            Assert.That(result.Value.ProcessedFiles, Is.LessThan(result.Value.TotalFiles));
            Assert.That(CollectCodes(result), Does.Contain(MessageCode.IntegrityErrorFormat));
        }
    }

    [Test]
    public async Task Verify_MissingManifest_FailsWithManifestRequired()
    {
        await using var provider = TestHost.CreateProvider();
        var orchestrator = provider.GetRequiredService<IBackupOrchestrator>();

        using var backup = new TempDir();
        _ = backup.WriteText("stray.txt", "not a manifest");

        var result = await orchestrator.ExecuteAsync(
            NewRequest(backup.Path, BackupOperation.Verify),
            new RecordingProgress<BackupStatus>()
        );

        Assert.That(CollectCodes(result), Does.Contain(MessageCode.ManifestRequiredForDecryption));
    }

    [Test]
    public async Task Verify_MissingSource_FailsWithSourcePathNotExist()
    {
        await using var provider = TestHost.CreateProvider();
        var orchestrator = provider.GetRequiredService<IBackupOrchestrator>();

        var missing = Path.Combine(Path.GetTempPath(), "bzc-missing", Guid.NewGuid().ToString("N"));

        var result = await orchestrator.ExecuteAsync(
            NewRequest(missing, BackupOperation.Verify),
            new RecordingProgress<BackupStatus>()
        );

        Assert.That(CollectCodes(result), Does.Contain(MessageCode.SourcePathNotExist));
    }

    [Test]
    public async Task Verify_EmptyPassword_ReportsPasswordRequired()
    {
        await using var provider = TestHost.CreateProvider();
        var orchestrator = provider.GetRequiredService<IBackupOrchestrator>();

        using var backup = new TempDir();

        var result = await orchestrator.ExecuteAsync(
            NewRequest(backup.Path, BackupOperation.Verify, password: string.Empty),
            new RecordingProgress<BackupStatus>()
        );

        Assert.That(CollectCodes(result), Does.Contain(MessageCode.PasswordRequired));
    }

    private static async Task CreateBackupAsync(
        IBackupOrchestrator orchestrator,
        TempDir source,
        TempDir destination,
        CompressionMode compression
    )
    {
        _ = source.WriteText("a.txt", new string('a', 4096));
        _ = source.WriteText("b.txt", new string('b', 8192));
        _ = source.WriteText(Path.Combine("sub", "c.txt"), "hello world");

        var result = await orchestrator.ExecuteAsync(
            NewRequest(source.Path, BackupOperation.Create, compression, destinationPath: destination.Path),
            new RecordingProgress<BackupStatus>()
        );

        Assert.That(result.IsSuccess && result.Value.IsSuccess, Is.True, "Backup creation failed.");
    }

    private static BackupRequest NewRequest(
        string sourcePath,
        BackupOperation operation,
        CompressionMode compression = CompressionMode.None,
        string password = Password,
        string destinationPath = ""
    )
    {
        return new BackupRequest(
            sourcePath,
            destinationPath,
            password,
            password,
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.PBKDF2,
            operation,
            compression,
            ProceedOnWarnings: true
        );
    }

    private static void CorruptOneChunk(string backupPath)
    {
        var chunksDir = Path.Combine(backupPath, BackupConstants.ChunksDirectoryName);
        var chunkFile = Directory.GetFiles(chunksDir, "*" + BackupConstants.AppFileExtension)[0];
        var bytes = File.ReadAllBytes(chunkFile);
        bytes[0] ^= 0xFF;
        File.WriteAllBytes(chunkFile, bytes);
    }

    private static HashSet<MessageCode> CollectCodes(Result<BackupResult> result)
    {
        var codes = result.Errors.Select(static e => e.Code).ToHashSet();
        if (result.IsSuccess)
        {
            foreach (var error in result.Value.Errors)
            {
                _ = codes.Add(error.Code);
            }
        }

        return codes;
    }
}
