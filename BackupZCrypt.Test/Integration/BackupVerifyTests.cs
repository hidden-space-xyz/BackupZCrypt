using BackupZCrypt.Application.Orchestrators.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.ValueObjects.Backup;
using BackupZCrypt.Domain.ValueObjects.Localization;
using BackupZCrypt.Test.Common;

using Microsoft.Extensions.DependencyInjection;

namespace BackupZCrypt.Test.Integration;

/// <summary>
/// Integration tests for the backup verify operation.
/// </summary>
public sealed class BackupVerifyTests
{
    /// <summary>
    /// The password every backup in this fixture is created and verified with; long and varied
    /// enough to clear the validator's strength warnings.
    /// </summary>
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

    /// <summary>
    /// Fills the source with three files, one of them nested, and backs them up so the verify tests
    /// have an intact backup to work against. Fails the test if creation does not succeed.
    /// </summary>
    /// <param name="orchestrator">The orchestrator that executes the create operation.</param>
    /// <param name="source">The directory the sample files are written to.</param>
    /// <param name="destination">The directory the backup is written to.</param>
    /// <param name="compression">The compression mode applied to chunks before encryption.</param>
    /// <returns>A task that completes once the backup exists.</returns>
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

    /// <summary>
    /// Builds an AES plus PBKDF2 request that proceeds past advisory warnings.
    /// </summary>
    /// <param name="sourcePath">The tree to back up, or the backup directory to verify.</param>
    /// <param name="operation">The operation to dispatch.</param>
    /// <param name="compression">The compression mode applied to chunks before encryption.</param>
    /// <param name="password">The password to derive keys from; defaults to the fixture password.</param>
    /// <param name="destinationPath">The output directory, left empty for read-only verify runs.</param>
    /// <returns>The assembled request.</returns>
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

    /// <summary>
    /// Flips every bit of the first byte of an arbitrary chunk file, so the authentication tag over
    /// that ciphertext no longer validates and verify must report an integrity failure.
    /// </summary>
    /// <param name="backupPath">The root of the backup whose chunk directory is tampered with.</param>
    private static void CorruptOneChunk(string backupPath)
    {
        var chunksDir = Path.Combine(backupPath, BackupConstants.ChunksDirectoryName);
        var chunkFile = Directory.GetFiles(chunksDir, "*" + BackupConstants.AppFileExtension)[0];
        var bytes = File.ReadAllBytes(chunkFile);
        bytes[0] ^= 0xFF;
        File.WriteAllBytes(chunkFile, bytes);
    }

    /// <summary>
    /// Gathers the orchestrator's own error codes together with the per-file codes of the inner
    /// backup result, which is only read when the outer result succeeded because
    /// <see cref="BackupZCrypt.Application.ValueObjects.Result{T}.Value"/> throws on a failure.
    /// </summary>
    /// <param name="result">The orchestrator outcome to inspect.</param>
    /// <returns>The distinct message codes reported at either level.</returns>
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
