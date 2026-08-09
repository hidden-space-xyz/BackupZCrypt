using System.Security.Cryptography;

using BackupZCrypt.Application.Commands;
using BackupZCrypt.Application.Commands.Interfaces;
using BackupZCrypt.Application.Queries;
using BackupZCrypt.Application.Queries.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Application.ValueObjects.Backup;
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
/// Verify reconstructs every file into <see cref="Stream.Null"/>, so it must neither write to any
/// directory nor rewrite, prune, or repair a single byte of the archive it is reading.
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

    [Theory]
    [InlineData(CompressionMode.None)]
    [InlineData(CompressionMode.Zstd)]
    internal async Task Verify_IntactBackup_SucceedsWithoutTouchingTheArchiveOrTheDestination(
        CompressionMode compression
    )
    {
        await using var provider = TestHost.CreateProvider();
        var createHandler = provider.GetRequiredService<ICommandHandler<CreateBackupCommand, Result<BackupOutcome>>>();
        var verifyHandler = provider.GetRequiredService<IQueryHandler<VerifyBackupQuery, Result<BackupOutcome>>>();

        using var source = new TempDir();
        using var destination = new TempDir();
        using var scratch = new TempDir();
        await CreateBackupAsync(createHandler, source, destination, compression);

        var archiveBefore = Snapshot(destination.Path);

        var result = await verifyHandler.HandleAsync(
            NewVerifyQuery(destination.Path),
            TestContext.Current.CancellationToken
        );

        Assert.Multiple(
            () => Assert.True(result.IsSuccess),
            () => Assert.True(result.Value.Completion!.IsSuccess),
            () => Assert.Equal(SourceFileCount, result.Value.Completion!.TotalFiles),
            () => Assert.Equal(result.Value.Completion!.TotalFiles, result.Value.Completion!.ProcessedFiles),
            () => Assert.Empty(result.Value.Completion!.Errors),
            () => Assert.Empty(Directory.GetFileSystemEntries(scratch.Path)),
            () => Assert.Equal(archiveBefore, Snapshot(destination.Path))
        );
    }

    [Fact]
    internal async Task Verify_WrongPassword_FailsWithInvalidPasswordAndReportsNoVerifiedFiles()
    {
        await using var provider = TestHost.CreateProvider();
        var createHandler = provider.GetRequiredService<ICommandHandler<CreateBackupCommand, Result<BackupOutcome>>>();
        var verifyHandler = provider.GetRequiredService<IQueryHandler<VerifyBackupQuery, Result<BackupOutcome>>>();

        using var source = new TempDir();
        using var destination = new TempDir();
        await CreateBackupAsync(createHandler, source, destination, CompressionMode.None);

        var result = await verifyHandler.HandleAsync(
            NewVerifyQuery(destination.Path, password: "a-different-password-1"),
            TestContext.Current.CancellationToken
        );

        Assert.Multiple(
            () => Assert.False(
                result.IsSuccess,
                "Verify with a wrong password unexpectedly succeeded. A wrong password stops verification "
                    + "before any file is read, so the outer result must fail outright rather than report a "
                    + "partial verification alongside the authentication failure."
            ),
            () => Assert.Contains(MessageCode.VerifyInvalidPassword, CollectCodes(result)),
            () => Assert.Single(CollectCodes(result))
        );
    }

    [Fact]
    internal async Task Verify_CorruptedChunk_ReportsIntegrityErrorForAffectedFile()
    {
        await using var provider = TestHost.CreateProvider();
        var createHandler = provider.GetRequiredService<ICommandHandler<CreateBackupCommand, Result<BackupOutcome>>>();
        var verifyHandler = provider.GetRequiredService<IQueryHandler<VerifyBackupQuery, Result<BackupOutcome>>>();

        using var source = new TempDir();
        using var destination = new TempDir();
        await CreateBackupAsync(createHandler, source, destination, CompressionMode.None);

        var chunkFile = ChunkFiles(destination.Path)[0];
        var bytes = await File.ReadAllBytesAsync(chunkFile, TestContext.Current.CancellationToken);
        bytes[0] ^= 0xFF;
        await File.WriteAllBytesAsync(chunkFile, bytes, TestContext.Current.CancellationToken);

        var archiveBefore = Snapshot(destination.Path);

        var result = await verifyHandler.HandleAsync(
            NewVerifyQuery(destination.Path),
            TestContext.Current.CancellationToken
        );

        Assert.Multiple(
            () => Assert.True(result.IsSuccess),
            () => Assert.False(result.Value.Completion!.IsSuccess),
            () => Assert.Equal(SourceFileCount, result.Value.Completion!.TotalFiles),
            () => Assert.Equal(SourceFileCount - 1, result.Value.Completion!.ProcessedFiles),
            () => Assert.Single(result.Value.Completion!.Errors),
            () => Assert.All(
                result.Value.Completion!.Errors,
                e => Assert.True(e.Code is MessageCode.IntegrityErrorFormat)
            ),
            () => Assert.Equal(archiveBefore, Snapshot(destination.Path))
        );
    }

    [Fact]
    internal async Task Verify_ChunkFileMissing_ReportsIntegrityErrorForAffectedFile()
    {
        await using var provider = TestHost.CreateProvider();
        var createHandler = provider.GetRequiredService<ICommandHandler<CreateBackupCommand, Result<BackupOutcome>>>();
        var verifyHandler = provider.GetRequiredService<IQueryHandler<VerifyBackupQuery, Result<BackupOutcome>>>();

        using var source = new TempDir();
        using var destination = new TempDir();
        await CreateBackupAsync(createHandler, source, destination, CompressionMode.None);

        File.Delete(ChunkFiles(destination.Path)[0]);

        var result = await verifyHandler.HandleAsync(
            NewVerifyQuery(destination.Path),
            TestContext.Current.CancellationToken
        );

        Assert.Multiple(
            () => Assert.True(result.IsSuccess),
            () => Assert.False(result.Value.Completion!.IsSuccess),
            () => Assert.Equal(SourceFileCount, result.Value.Completion!.TotalFiles),
            () => Assert.Equal(SourceFileCount - 1, result.Value.Completion!.ProcessedFiles),
            () => Assert.Single(result.Value.Completion!.Errors),
            () => Assert.All(
                result.Value.Completion!.Errors,
                e => Assert.True(e.Code is MessageCode.IntegrityErrorFormat)
            )
        );
    }

    [Fact]
    internal async Task Verify_MissingManifest_FailsWithManifestRequired()
    {
        await using var provider = TestHost.CreateProvider();
        var verifyHandler = provider.GetRequiredService<IQueryHandler<VerifyBackupQuery, Result<BackupOutcome>>>();

        using var backup = new TempDir();
        _ = backup.WriteText("stray.txt", "not a manifest");

        var result = await verifyHandler.HandleAsync(
            NewVerifyQuery(backup.Path),
            TestContext.Current.CancellationToken
        );

        Assert.Contains(MessageCode.ManifestRequiredForDecryption, CollectCodes(result));
    }

    [Fact]
    internal async Task Verify_MissingSource_FailsWithSourcePathNotExist()
    {
        await using var provider = TestHost.CreateProvider();
        var verifyHandler = provider.GetRequiredService<IQueryHandler<VerifyBackupQuery, Result<BackupOutcome>>>();

        var missing = Path.Combine(Path.GetTempPath(), "bzc-missing", Guid.NewGuid().ToString("N"));

        var result = await verifyHandler.HandleAsync(
            NewVerifyQuery(missing),
            TestContext.Current.CancellationToken
        );

        Assert.Contains(MessageCode.SourcePathNotExist, CollectCodes(result));
    }

    [Fact]
    internal async Task Verify_EmptyPassword_ReportsPasswordRequired()
    {
        await using var provider = TestHost.CreateProvider();
        var verifyHandler = provider.GetRequiredService<IQueryHandler<VerifyBackupQuery, Result<BackupOutcome>>>();

        using var backup = new TempDir();

        var result = await verifyHandler.HandleAsync(
            NewVerifyQuery(backup.Path, password: string.Empty),
            TestContext.Current.CancellationToken
        );

        Assert.Contains(MessageCode.PasswordRequired, CollectCodes(result));
    }

    /// <summary>
    /// Fills the source with three files of distinct content, one of them nested, and backs them up
    /// so the verify tests have an intact backup to work against. Fails the test if creation does not
    /// succeed.
    /// </summary>
    /// <param name="createHandler">The handler that executes the create command.</param>
    /// <param name="source">The directory the sample files are written to.</param>
    /// <param name="destination">The directory the backup is written to.</param>
    /// <param name="compression">The compression mode applied to chunks before encryption.</param>
    /// <returns>A task that completes once the backup exists.</returns>
    private static async Task CreateBackupAsync(
        ICommandHandler<CreateBackupCommand, Result<BackupOutcome>> createHandler,
        TempDir source,
        TempDir destination,
        CompressionMode compression
    )
    {
        _ = source.WriteText("a.txt", new string('a', 4096));
        _ = source.WriteText("b.txt", new string('b', 8192));
        _ = source.WriteText(Path.Combine("sub", "c.txt"), "hello world");

        var result = await createHandler.HandleAsync(NewCreateCommand(source.Path, destination.Path, compression));

        Assert.Multiple(
            () => Assert.True(result.IsSuccess && result.Value.Completion!.IsSuccess, "Backup creation failed."),
            () => Assert.Equal(SourceFileCount, ChunkFiles(destination.Path).Length)
        );
    }

    /// <summary>
    /// Builds an AES plus PBKDF2 create command that proceeds past advisory warnings and reports
    /// progress to a throwaway sink.
    /// </summary>
    /// <param name="sourcePath">The tree to back up.</param>
    /// <param name="destinationPath">The directory the backup is written to.</param>
    /// <param name="compression">The compression mode applied to chunks before encryption.</param>
    /// <returns>The assembled command.</returns>
    private static CreateBackupCommand NewCreateCommand(string sourcePath, string destinationPath, CompressionMode compression)
    {
        return new CreateBackupCommand(
            sourcePath,
            destinationPath,
            Password,
            Password,
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.PBKDF2,
            compression,
            ProceedOnWarnings: true
        )
        {
            Progress = new RecordingProgress<BackupStatus>(),
        };
    }

    /// <summary>
    /// Builds a verify query that reports progress to a throwaway sink.
    /// </summary>
    /// <param name="backupPath">The backup directory to verify.</param>
    /// <param name="password">The password to derive keys from; defaults to the fixture password.</param>
    /// <returns>The assembled query.</returns>
    private static VerifyBackupQuery NewVerifyQuery(string backupPath, string password = Password)
    {
        return new VerifyBackupQuery(backupPath, password)
        {
            Progress = new RecordingProgress<BackupStatus>(),
        };
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
    /// Gathers the handler's own error codes together with the per-file codes of the completed engine
    /// result, which is only read when the outer result succeeded with a completion because
    /// <see cref="BackupZCrypt.Application.ValueObjects.Result{T}.Value"/> throws on a failure.
    /// </summary>
    /// <param name="result">The handler outcome to inspect.</param>
    /// <returns>The distinct message codes reported at either level.</returns>
    private static HashSet<MessageCode> CollectCodes(Result<BackupOutcome> result)
    {
        var codes = result.Errors.Select(static e => e.Code).ToHashSet();
        if (result.IsSuccess && result.Value.Completion is not null)
        {
            foreach (var error in result.Value.Completion.Errors)
            {
                _ = codes.Add(error.Code);
            }
        }

        return codes;
    }
}
