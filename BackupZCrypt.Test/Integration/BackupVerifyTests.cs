using System.Security.Cryptography;

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
/// <remarks>
/// Verify reconstructs every file into <see cref="Stream.Null"/>, so it must neither write to the
/// destination it was handed nor rewrite, prune, or repair a single byte of the archive it is reading.
/// </remarks>
public sealed class BackupVerifyTests
{
    /// <summary>
    /// The password every backup in this fixture is created and verified with; long and varied
    /// enough to clear the validator's strength warnings.
    /// </summary>
    private const string Password = "Correct-Horse-Battery-Staple-42";

    /// <summary>
    /// The number of files <see cref="CreateBackupAsync"/> puts in the source tree; each has distinct
    /// content, so the backup holds exactly one chunk per file and damaging one chunk can only ever
    /// affect one file.
    /// </summary>
    private const int SourceFileCount = 3;

    [TestCase(CompressionMode.None)]
    [TestCase(CompressionMode.Zstd)]
    public async Task Verify_IntactBackup_SucceedsWithoutTouchingTheArchiveOrTheDestination(
        CompressionMode compression
    )
    {
        await using var provider = TestHost.CreateProvider();
        var orchestrator = provider.GetRequiredService<IBackupOrchestrator>();

        using var source = new TempDir();
        using var destination = new TempDir();
        using var scratch = new TempDir();
        await CreateBackupAsync(orchestrator, source, destination, compression);

        var archiveBefore = Snapshot(destination.Path);

        var result = await orchestrator.ExecuteAsync(
            NewRequest(
                destination.Path,
                BackupOperation.Verify,
                compression,
                destinationPath: scratch.Path
            ),
            new RecordingProgress<BackupStatus>()
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.IsSuccess, Is.True);
            Assert.That(result.Value.TotalFiles, Is.EqualTo(SourceFileCount));
            Assert.That(result.Value.ProcessedFiles, Is.EqualTo(result.Value.TotalFiles));
            Assert.That(result.Value.Errors, Is.Empty);

            Assert.That(
                Directory.GetFileSystemEntries(scratch.Path),
                Is.Empty,
                "Verify wrote to the destination directory."
            );
            Assert.That(
                Snapshot(destination.Path),
                Is.EqualTo(archiveBefore),
                "Verify mutated the archive it was asked to read."
            );
        }
    }

    [Test]
    public async Task Verify_WrongPassword_FailsWithInvalidPasswordAndReportsNoVerifiedFiles()
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

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.IsSuccess,
                Is.False,
                "Verify with a wrong password unexpectedly succeeded. A wrong password stops verification "
                    + "before any file is read, so the outer result must fail outright rather than report a "
                    + "partial verification alongside the authentication failure."
            );
            Assert.That(CollectCodes(result), Does.Contain(MessageCode.VerifyInvalidPassword));
            Assert.That(CollectCodes(result), Has.Count.EqualTo(1), "Verify reported more than the auth failure.");
        }
    }

    [Test]
    public async Task Verify_CorruptedChunk_ReportsIntegrityErrorForAffectedFile()
    {
        await using var provider = TestHost.CreateProvider();
        var orchestrator = provider.GetRequiredService<IBackupOrchestrator>();

        using var source = new TempDir();
        using var destination = new TempDir();
        await CreateBackupAsync(orchestrator, source, destination, CompressionMode.None);

        var chunkFile = ChunkFiles(destination.Path)[0];
        var bytes = await File.ReadAllBytesAsync(chunkFile);
        bytes[0] ^= 0xFF;
        await File.WriteAllBytesAsync(chunkFile, bytes);

        var archiveBefore = Snapshot(destination.Path);

        var result = await orchestrator.ExecuteAsync(
            NewRequest(destination.Path, BackupOperation.Verify),
            new RecordingProgress<BackupStatus>()
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.IsSuccess, Is.False);
            Assert.That(result.Value.TotalFiles, Is.EqualTo(SourceFileCount));
            Assert.That(result.Value.ProcessedFiles, Is.EqualTo(SourceFileCount - 1));
            Assert.That(result.Value.Errors, Has.Count.EqualTo(1));
            Assert.That(
                result.Value.Errors,
                Has.All.Matches<LocalizableMessage>(e => e.Code == MessageCode.IntegrityErrorFormat)
            );
            Assert.That(
                Snapshot(destination.Path),
                Is.EqualTo(archiveBefore),
                "Verify altered the damaged archive instead of only reporting on it."
            );
        }
    }

    [Test]
    public async Task Verify_ChunkFileMissing_ReportsIntegrityErrorForAffectedFile()
    {
        await using var provider = TestHost.CreateProvider();
        var orchestrator = provider.GetRequiredService<IBackupOrchestrator>();

        using var source = new TempDir();
        using var destination = new TempDir();
        await CreateBackupAsync(orchestrator, source, destination, CompressionMode.None);

        File.Delete(ChunkFiles(destination.Path)[0]);

        var result = await orchestrator.ExecuteAsync(
            NewRequest(destination.Path, BackupOperation.Verify),
            new RecordingProgress<BackupStatus>()
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.IsSuccess, Is.False);
            Assert.That(result.Value.TotalFiles, Is.EqualTo(SourceFileCount));
            Assert.That(
                result.Value.ProcessedFiles,
                Is.EqualTo(SourceFileCount - 1),
                "A chunk that is absent rather than corrupt surfaces as an I/O failure instead of a "
                    + "cryptographic one, and that arm of the per-file error handling must still finish the "
                    + "run and salvage every file whose chunks are intact."
            );
            Assert.That(result.Value.Errors, Has.Count.EqualTo(1));
            Assert.That(
                result.Value.Errors,
                Has.All.Matches<LocalizableMessage>(e => e.Code == MessageCode.IntegrityErrorFormat)
            );
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
    /// Fills the source with three files of distinct content, one of them nested, and backs them up
    /// so the verify tests have an intact backup to work against. Fails the test if creation does not
    /// succeed.
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

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess && result.Value.IsSuccess, Is.True, "Backup creation failed.");
            Assert.That(
                ChunkFiles(destination.Path),
                Has.Length.EqualTo(SourceFileCount),
                "The fixture assumes one chunk per file, so damaging one chunk affects exactly one file."
            );
        }
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
    /// Lists the encrypted chunk files stored under a backup root, in a stable order so the tests
    /// always damage the same chunk.
    /// </summary>
    /// <param name="backupPath">The root of the backup whose chunk directory is inspected.</param>
    /// <returns>The absolute paths of the stored chunk files, ordered by name.</returns>
    private static string[] ChunkFiles(string backupPath)
    {
        return
        [
            .. Directory
                .GetFiles(
                    Path.Combine(backupPath, BackupConstants.ChunksDirectoryName),
                    "*" + BackupConstants.AppFileExtension
                )
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// Captures the exact content of every file under a directory, so a later comparison detects any
    /// write, deletion, or addition without depending on file system timestamps.
    /// </summary>
    /// <param name="root">The directory to snapshot.</param>
    /// <returns>One ordered entry per file, pairing its relative path with the hash of its bytes.</returns>
    private static string[] Snapshot(string root)
    {
        return
        [
            .. Directory
                .GetFiles(root, "*", SearchOption.AllDirectories)
                .Select(file =>
                    Path.GetRelativePath(root, file).Replace('\\', '/')
                    + "|"
                    + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)))
                )
                .Order(StringComparer.Ordinal),
        ];
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
