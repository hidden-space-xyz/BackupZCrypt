using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

using BackupZCrypt.Application.Commands;
using BackupZCrypt.Application.Commands.Interfaces;
using BackupZCrypt.Application.Queries;
using BackupZCrypt.Application.Queries.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Application.ValueObjects.Backup;
using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Services.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Backup;
using BackupZCrypt.Domain.ValueObjects.Localization;
using BackupZCrypt.Test.Common;

using Microsoft.Extensions.DependencyInjection;

namespace BackupZCrypt.Test.Integration;

/// <summary>
/// End-to-end regression tests for the data-safety guarantees of create, update, restore, and verify:
/// no user file is ever deleted, a backup is only replaced once its successor is complete, nothing the
/// user selected is left out silently, and a restore reproduces the files with their metadata.
/// </summary>
/// <remarks>
/// Every case drives the real handlers over real temporary folders, because each guarantee depends on
/// the validator, the orchestrator, and the engine agreeing with one another.
/// </remarks>
public sealed class BackupSafetyTests
{
    /// <summary>
    /// The password every backup in this fixture is created with.
    /// </summary>
    private const string Password = "Correct-Horse-Battery-Staple-42";

    [Fact]
    internal async Task Create_IntoAFolderHoldingUserFiles_IsRefusedAndDeletesNothing()
    {
        await using var provider = TestHost.CreateProvider();
        using var source = new TempDir();
        using var destination = new TempDir();
        _ = source.WriteText("a.txt", "source");
        var thesis = destination.WriteText("thesis.docx", "irreplaceable");

        var result = await CreateAsync(provider, source.Path, destination.Path);

        Assert.Multiple(
            () => Assert.False(result.IsSuccess),
            () => Assert.Contains(MessageCode.DestinationNotEmpty, result.Errors.Select(static e => e.Code)),
            () => Assert.Equal("irreplaceable", File.ReadAllText(thesis)),
            () => Assert.Equal([thesis], Directory.GetFileSystemEntries(destination.Path))
        );
    }

    [Fact]
    internal async Task Create_OverAnExistingBackup_ReplacesItAndPrunesTheOldChunks()
    {
        await using var provider = TestHost.CreateProvider();
        using var firstSource = new TempDir();
        using var secondSource = new TempDir();
        using var destination = new TempDir();
        using var restored = new TempDir();
        _ = firstSource.WriteText("old.txt", "first generation");
        _ = secondSource.WriteText("new.txt", "second generation");

        AssertCompleted(await CreateAsync(provider, firstSource.Path, destination.Path));
        var replacement = await CreateAsync(provider, secondSource.Path, destination.Path);
        AssertCompleted(await RestoreAsync(provider, destination.Path, restored.Path));

        Assert.Multiple(
            () => AssertCompleted(replacement),
            () => Assert.Equal(["new.txt"], RelativeFiles(restored.Path)),
            () => Assert.Single(ChunkFiles(destination.Path))
        );
    }

    [Fact]
    internal async Task Create_OverAnExistingBackupCancelledPartWay_LeavesTheOldBackupIntact()
    {
        await using var provider = TestHost.CreateProvider();
        using var firstSource = new TempDir();
        using var secondSource = new TempDir();
        using var destination = new TempDir();
        using var restored = new TempDir();
        _ = firstSource.WriteText("old.txt", "first generation");
        for (var i = 0; i < 40; i++)
        {
            _ = secondSource.WriteFile($"file{i}.bin", RandomNumberGenerator.GetBytes(64 * 1024));
        }

        AssertCompleted(await CreateAsync(provider, firstSource.Path, destination.Path));
        var before = Snapshot(destination.Path);

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var progress = new CallbackProgress(status =>
        {
            if (status.ProcessedFiles > 0)
            {
                cancellation.Cancel();
            }
        });

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider
                .GetRequiredService<ICommandHandler<CreateBackupCommand, Result<BackupOutcome>>>()
                .HandleAsync(
                    new CreateBackupCommand(
                        secondSource.Path,
                        destination.Path,
                        Password,
                        Password,
                        EncryptionAlgorithm.Aes,
                        KeyDerivationAlgorithm.PBKDF2,
                        CompressionMode.None,
                        ProceedOnWarnings: true
                    )
                    {
                        Progress = progress,
                    },
                    cancellation.Token
                )
        );

        AssertCompleted(await RestoreAsync(provider, destination.Path, restored.Path));

        Assert.Multiple(
            () => Assert.Equal(before, Snapshot(destination.Path)),
            () => Assert.Equal(["old.txt"], RelativeFiles(restored.Path))
        );
    }

    [Fact]
    internal async Task Create_WhenNoFileCanBeRead_WritesNoManifestAndKeepsTheOldBackup()
    {
        await using var provider = TestHost.CreateProvider();
        using var firstSource = new TempDir();
        using var secondSource = new TempDir();
        using var destination = new TempDir();
        _ = firstSource.WriteText("old.txt", "first generation");
        var locked = secondSource.WriteText("locked.txt", "held open exclusively");

        AssertCompleted(await CreateAsync(provider, firstSource.Path, destination.Path));
        var before = Snapshot(destination.Path);

        Result<BackupOutcome> result;
        await using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            result = await CreateAsync(provider, secondSource.Path, destination.Path);
        }

        Assert.Multiple(
            () => Assert.False(result.IsSuccess),
            () => Assert.Contains(MessageCode.AllFilesFailed, result.Errors.Select(static e => e.Code)),
            () => Assert.Equal(before, Snapshot(destination.Path))
        );
    }

    [Fact]
    internal async Task Create_FileThatGrowsAfterItWasMeasured_IsBackedUpAsReadInsteadOfFailingTheRun()
    {
        await using var provider = TestHost.CreateProvider();
        using var source = new TempDir();
        using var destination = new TempDir();
        using var restored = new TempDir();
        var log = source.WriteText("app.log", "first line\n");
        _ = source.WriteText("other.txt", "stable");

        var grown = false;
        var progress = new CallbackProgress(_ =>
        {
            if (!grown)
            {
                grown = true;
                File.AppendAllText(log, new string('x', 3 * 1024 * 1024));
            }
        });

        var result = await CreateAsync(provider, source.Path, destination.Path, progress: progress);
        AssertCompleted(await RestoreAsync(provider, destination.Path, restored.Path));

        var original = await File.ReadAllTextAsync(log, TestContext.Current.CancellationToken);
        var copy = await File.ReadAllTextAsync(Path.Join(restored.Path, "app.log"), TestContext.Current.CancellationToken);

        Assert.Multiple(
            () => AssertCompleted(result),
            () => Assert.Equal(original, copy)
        );
    }

    [Fact]
    internal async Task Create_FileAnotherProgramKeepsOpenForWriting_IsBackedUp()
    {
        await using var provider = TestHost.CreateProvider();
        using var source = new TempDir();
        using var destination = new TempDir();
        var database = source.WriteText("app.db", "live data");

        Result<BackupOutcome> result;
        await using (new FileStream(database, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
        {
            result = await CreateAsync(provider, source.Path, destination.Path);
        }

        AssertCompleted(result);
    }

    [Fact]
    internal async Task Create_NamesThatOnlyDifferInTheirUnicodeForm_SkipsOnlyTheSecondOne()
    {
        await using var provider = TestHost.CreateProvider();
        using var source = new TempDir();
        using var destination = new TempDir();
        _ = source.WriteText("café.txt", "composed");
        _ = source.WriteText("other.txt", "unrelated");

        try
        {
            _ = source.WriteText("café.txt", "decomposed");
        }
        catch (IOException)
        {
            Assert.Skip("This file system treats both Unicode forms as the same name.");
        }

        if (Directory.GetFiles(source.Path).Length < 3)
        {
            Assert.Skip("This file system treats both Unicode forms as the same name.");
        }

        var result = await CreateAsync(provider, source.Path, destination.Path);
        var completion = result.Value.Completion!;

        Assert.Multiple(
            () => Assert.True(result.IsSuccess),
            () => Assert.Equal(2, completion.ProcessedFiles),
            () => Assert.Equal([MessageCode.NameCollisionFormat], completion.Errors.Select(static e => e.Code))
        );
    }

    [Fact]
    internal async Task Create_ProgressOfASingleLargeFile_AdvancesBeforeTheFileCompletes()
    {
        await using var provider = TestHost.CreateProvider();
        using var source = new TempDir();
        using var destination = new TempDir();
        _ = source.WriteFile("large.bin", RandomNumberGenerator.GetBytes(24 * 1024 * 1024));

        var progress = new RecordingProgress<BackupStatus>();
        AssertCompleted(await CreateAsync(provider, source.Path, destination.Path, progress: progress));

        Assert.Contains(progress.Reports, static r => r.ProcessedFiles is 0 && r.ProcessedBytes > 0);
    }

    [Fact]
    internal async Task Update_FileDeletedFromTheSource_IsReportedAsRemovedFromTheBackup()
    {
        await using var provider = TestHost.CreateProvider();
        using var source = new TempDir();
        using var destination = new TempDir();
        _ = source.WriteText("keep.txt", "keep");
        var doomed = source.WriteText("doomed.txt", "doomed");

        AssertCompleted(await CreateAsync(provider, source.Path, destination.Path));
        File.Delete(doomed);

        var preview = await UpdateAsync(provider, source.Path, destination.Path, proceedOnWarnings: false);
        var update = await UpdateAsync(provider, source.Path, destination.Path);

        Assert.Multiple(
            () =>
                Assert.Equal(
                    [MessageCode.UpdateRemovesFilesFormat],
                    preview.Value.PendingWarnings.Select(static w => w.Code)
                ),
            () => Assert.Equal(1, update.Value.Completion!.RemovedFiles),
            () => Assert.Equal(1, update.Value.Completion!.UnchangedFiles),
            () => Assert.Equal(0, update.Value.Completion!.TotalFiles)
        );
    }

    [Fact]
    internal async Task Update_UnchangedFile_IsCarriedOverWithoutBeingRead()
    {
        await using var provider = TestHost.CreateProvider();
        using var source = new TempDir();
        using var destination = new TempDir();
        var unchanged = source.WriteText("unchanged.txt", "same as before");

        AssertCompleted(await CreateAsync(provider, source.Path, destination.Path));

        Result<BackupOutcome> update;
        await using (new FileStream(unchanged, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            update = await UpdateAsync(provider, source.Path, destination.Path);
        }

        Assert.Multiple(
            () => AssertCompleted(update),
            () => Assert.Equal(1, update.Value.Completion!.UnchangedFiles),
            () => Assert.Equal(0, update.Value.Completion!.TotalFiles)
        );
    }

    [Fact]
    internal async Task Update_WhileAnotherOperationHoldsTheBackup_ReportsBackupInUse()
    {
        await using var provider = TestHost.CreateProvider();
        using var source = new TempDir();
        using var destination = new TempDir();
        _ = source.WriteText("a.txt", "a");

        AssertCompleted(await CreateAsync(provider, source.Path, destination.Path));

        var fileOperations = provider.GetRequiredService<IFileOperationsService>();
        Result<BackupOutcome> update;
        using (fileOperations.AcquireExclusiveLock(Path.Join(destination.Path, BackupConstants.LockFileName)))
        {
            update = await UpdateAsync(provider, source.Path, destination.Path);
        }

        Assert.Equal([MessageCode.BackupInUse], update.Errors.Select(static e => e.Code));
    }

    [Fact]
    internal async Task CreateAndUpdate_FolderThatCannotBeRead_IsReportedAndItsFilesStayInTheBackup()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The folder permission is set through Windows access control lists.");
        }

        await using var provider = TestHost.CreateProvider();
        using var source = new TempDir();
        using var destination = new TempDir();
        using var restored = new TempDir();
        _ = source.WriteText("visible.txt", "visible");
        _ = source.WriteText(Path.Join("secret", "inside.txt"), "inside");

        AssertCompleted(await CreateAsync(provider, source.Path, destination.Path));

        Result<BackupOutcome> update;
        using (DenyListing(source.Combine("secret")))
        {
            update = await UpdateAsync(provider, source.Path, destination.Path);
        }

        AssertCompleted(await RestoreAsync(provider, destination.Path, restored.Path));

        Assert.Multiple(
            () => Assert.Equal(0, update.Value.Completion!.RemovedFiles),
            () =>
                Assert.Equal(
                    [MessageCode.InaccessibleFolderFormat],
                    update.Value.Completion!.Errors.Select(static e => e.Code)
                ),
            () => Assert.Equal(["secret/inside.txt", "visible.txt"], RelativeFiles(restored.Path))
        );
    }

    [Fact]
    internal async Task Restore_IntoAFolderThatContainsTheBackup_RestoresWithoutTouchingTheBackup()
    {
        await using var provider = TestHost.CreateProvider();
        using var source = new TempDir();
        using var root = new TempDir();
        _ = source.WriteText(Path.Join("docs", "a.txt"), "a");
        var backup = root.Combine("backups", "laptop");

        AssertCompleted(await CreateAsync(provider, source.Path, backup));
        var before = Snapshot(backup);

        AssertCompleted(await RestoreAsync(provider, backup, root.Path));

        Assert.Multiple(
            () => Assert.Equal("a", File.ReadAllText(root.Combine("docs", "a.txt"))),
            () => Assert.Equal(before, Snapshot(backup))
        );
    }

    [Fact]
    internal async Task Restore_WrongPassword_CreatesNoDestinationFolder()
    {
        await using var provider = TestHost.CreateProvider();
        using var source = new TempDir();
        using var destination = new TempDir();
        using var area = new TempDir();
        _ = source.WriteText("a.txt", "a");
        var target = area.Combine("restore-here");

        AssertCompleted(await CreateAsync(provider, source.Path, destination.Path));
        var result = await RestoreAsync(provider, destination.Path, target, password: "not-the-password");

        Assert.Multiple(
            () => Assert.Equal([MessageCode.InvalidPassword], result.Errors.Select(static e => e.Code)),
            () => Assert.False(Directory.Exists(target))
        );
    }

    [Fact]
    internal async Task Restore_PasswordTypedInAnotherUnicodeForm_OpensTheBackup()
    {
        await using var provider = TestHost.CreateProvider();
        using var source = new TempDir();
        using var destination = new TempDir();
        using var restored = new TempDir();
        _ = source.WriteText("a.txt", "a");
        var composed = "Contraseña-segura-2025!";
        var decomposed = composed.Normalize(NormalizationForm.FormD);

        AssertCompleted(await CreateAsync(provider, source.Path, destination.Path, password: composed));

        AssertCompleted(await RestoreAsync(provider, destination.Path, restored.Path, password: decomposed));
    }

    [Fact]
    internal async Task RoundTrip_RestoresModificationTimesAttributesAndEmptyFolders()
    {
        await using var provider = TestHost.CreateProvider();
        using var source = new TempDir();
        using var destination = new TempDir();
        using var restored = new TempDir();
        var dated = source.WriteText("dated.txt", "dated");
        var readOnly = source.WriteText("read-only.txt", "locked");
        _ = Directory.CreateDirectory(source.Combine("empty", "nested"));
        var recorded = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(dated, recorded);
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);

        try
        {
            AssertCompleted(await CreateAsync(provider, source.Path, destination.Path));
            AssertCompleted(await RestoreAsync(provider, destination.Path, restored.Path));

            Assert.Multiple(
                () => Assert.Equal(recorded, File.GetLastWriteTimeUtc(restored.Combine("dated.txt"))),
                () => Assert.True(File.GetAttributes(restored.Combine("read-only.txt")).HasFlag(FileAttributes.ReadOnly)),
                () => Assert.True(Directory.Exists(restored.Combine("empty", "nested")))
            );
        }
        finally
        {
            File.SetAttributes(readOnly, FileAttributes.Normal);
            ClearReadOnly(restored.Path);
        }
    }

    [Fact]
    internal async Task Restore_TwiceIntoTheSameFolder_ReplacesReadOnlyFilesItRestoredBefore()
    {
        await using var provider = TestHost.CreateProvider();
        using var source = new TempDir();
        using var destination = new TempDir();
        using var restored = new TempDir();
        var readOnly = source.WriteText("read-only.txt", "locked");
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);

        try
        {
            AssertCompleted(await CreateAsync(provider, source.Path, destination.Path));
            AssertCompleted(await RestoreAsync(provider, destination.Path, restored.Path));
            AssertCompleted(await RestoreAsync(provider, destination.Path, restored.Path));
        }
        finally
        {
            File.SetAttributes(readOnly, FileAttributes.Normal);
            ClearReadOnly(restored.Path);
        }
    }

    /// <summary>
    /// Runs a create through its handler.
    /// </summary>
    /// <param name="provider">The service provider.</param>
    /// <param name="source">The folder to back up.</param>
    /// <param name="destination">The backup folder.</param>
    /// <param name="password">The password.</param>
    /// <param name="progress">The progress sink, or <see langword="null"/> for a throwaway one.</param>
    /// <returns>The handler's result.</returns>
    private static Task<Result<BackupOutcome>> CreateAsync(
        IServiceProvider provider,
        string source,
        string destination,
        string password = Password,
        IProgress<BackupStatus>? progress = null
    )
    {
        return provider
            .GetRequiredService<ICommandHandler<CreateBackupCommand, Result<BackupOutcome>>>()
            .HandleAsync(
                new CreateBackupCommand(
                    source,
                    destination,
                    password,
                    password,
                    EncryptionAlgorithm.Aes,
                    KeyDerivationAlgorithm.PBKDF2,
                    CompressionMode.None,
                    ProceedOnWarnings: true
                )
                {
                    Progress = progress ?? new RecordingProgress<BackupStatus>(),
                },
                TestContext.Current.CancellationToken
            );
    }

    /// <summary>
    /// Runs an update through its handler.
    /// </summary>
    /// <param name="provider">The service provider.</param>
    /// <param name="source">The folder the update reads.</param>
    /// <param name="destination">The backup folder.</param>
    /// <param name="proceedOnWarnings">Whether to proceed past warnings.</param>
    /// <returns>The handler's result.</returns>
    private static Task<Result<BackupOutcome>> UpdateAsync(
        IServiceProvider provider,
        string source,
        string destination,
        bool proceedOnWarnings = true
    )
    {
        return provider
            .GetRequiredService<ICommandHandler<UpdateBackupCommand, Result<BackupOutcome>>>()
            .HandleAsync(
                new UpdateBackupCommand(source, destination, Password, proceedOnWarnings)
                {
                    Progress = new RecordingProgress<BackupStatus>(),
                },
                TestContext.Current.CancellationToken
            );
    }

    /// <summary>
    /// Runs a restore through its handler, past any warning.
    /// </summary>
    /// <param name="provider">The service provider.</param>
    /// <param name="backup">The backup folder.</param>
    /// <param name="destination">The restore folder.</param>
    /// <param name="password">The password.</param>
    /// <returns>The handler's result.</returns>
    private static Task<Result<BackupOutcome>> RestoreAsync(
        IServiceProvider provider,
        string backup,
        string destination,
        string password = Password
    )
    {
        return provider
            .GetRequiredService<ICommandHandler<RestoreBackupCommand, Result<BackupOutcome>>>()
            .HandleAsync(
                new RestoreBackupCommand(backup, destination, password, ProceedOnWarnings: true)
                {
                    Progress = new RecordingProgress<BackupStatus>(),
                },
                TestContext.Current.CancellationToken
            );
    }

    /// <summary>
    /// Asserts that an operation ran and every file in it succeeded.
    /// </summary>
    /// <param name="result">The handler's result.</param>
    private static void AssertCompleted(Result<BackupOutcome> result)
    {
        Assert.True(
            result.IsSuccess && result.Value.Completion is { IsSuccess: true },
            result.IsSuccess
                ? string.Join(", ", result.Value.Completion?.Errors.Select(static e => e.Code) ?? [])
                : string.Join(", ", result.Errors.Select(static e => e.Code))
        );
    }

    /// <summary>
    /// Lists the files under a folder relative to it, with forward slashes, in ordinal order.
    /// </summary>
    /// <param name="root">The folder to list.</param>
    /// <returns>The relative file paths.</returns>
    private static string[] RelativeFiles(string root)
    {
        return
        [
            .. Directory
                .GetFiles(root, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// Lists the chunk files of a backup.
    /// </summary>
    /// <param name="backup">The backup folder.</param>
    /// <returns>The chunk file paths.</returns>
    private static string[] ChunkFiles(string backup)
    {
        return Directory.GetFiles(
            Path.Join(backup, BackupConstants.ChunksDirectoryName),
            "*" + BackupConstants.AppFileExtension
        );
    }

    /// <summary>
    /// Captures the content of every file under a folder.
    /// </summary>
    /// <param name="root">The folder to capture.</param>
    /// <returns>One entry per file pairing its relative path with the hash of its bytes.</returns>
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
    /// Clears the read-only attribute of every file under a folder so it can be cleaned up.
    /// </summary>
    /// <param name="root">The folder to clean.</param>
    private static void ClearReadOnly(string root)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
    }

    /// <summary>
    /// Denies the current user the right to list a folder until the returned handle is disposed.
    /// </summary>
    /// <param name="directory">The folder to lock.</param>
    /// <returns>A handle that restores access.</returns>
    private static DeniedListing DenyListing(string directory)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException();
        }

        var info = new DirectoryInfo(directory);
        var rule = new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().User!,
            FileSystemRights.ListDirectory,
            AccessControlType.Deny
        );
        var security = info.GetAccessControl();
        security.AddAccessRule(rule);
        info.SetAccessControl(security);

        return new DeniedListing(info, rule);
    }

    /// <summary>
    /// Removes a deny rule from a folder when disposed.
    /// </summary>
    /// <param name="directory">The folder.</param>
    /// <param name="rule">The deny rule to remove.</param>
    private sealed class DeniedListing(DirectoryInfo directory, FileSystemAccessRule rule) : IDisposable
    {
        /// <summary>
        /// Restores access to the folder.
        /// </summary>
        public void Dispose()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            var security = directory.GetAccessControl();
            _ = security.RemoveAccessRule(rule);
            directory.SetAccessControl(security);
        }
    }

    /// <summary>
    /// A progress sink that runs a callback for every report, on the reporting thread.
    /// </summary>
    /// <param name="callback">The callback to run.</param>
    private sealed class CallbackProgress(Action<BackupStatus> callback) : IProgress<BackupStatus>
    {
        /// <summary>
        /// Serializes the callback.
        /// </summary>
        private readonly Lock gate = new();

        /// <inheritdoc/>
        public void Report(BackupStatus value)
        {
            lock (this.gate)
            {
                callback(value);
            }
        }
    }
}
