using System.Security.Cryptography;

using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.ValueObjects.Backup;
using BackupZCrypt.Domain.ValueObjects.Localization;
using BackupZCrypt.Test.Common;

using Microsoft.Extensions.DependencyInjection;

namespace BackupZCrypt.Test.Integration;

/// <summary>
/// Integration tests for the error, cancellation, and partial-failure paths of the chunked backup
/// engine: what happens to a user's data when one file cannot be read, a chunk is missing or
/// truncated, the destination already holds files, or the operation is cancelled part way through.
/// The happy paths are covered elsewhere; every case here asserts the resulting on-disk state, not
/// just the reported counts.
/// </summary>
/// <remarks>
/// <para>
/// Faults are injected deterministically through the progress sink. Every operation emits one
/// baseline report before it starts its parallel file loop, so a hook that runs on the first report
/// is guaranteed to observe a state in which no file has been opened yet — which makes "this file
/// vanished between enumeration and reading" and "the token was cancelled mid-operation" reproducible
/// without timers, sleeps, or any dependence on thread scheduling. The same hook serializes the
/// reports it forwards, so the recorded list stays intact while the engine reports from several
/// threads at once. A case that has to reach an update's parallel loop revises a source file first,
/// so the operation does not short-circuit on a tree that has not changed.
/// </para>
/// <para>
/// Deleting a file is preferred over permission or attribute tricks because read-only files, locked
/// files, and directory permissions behave differently on Windows and Linux, and the assertions here
/// must hold on both. Everything runs against the real crypto stack with PBKDF2 (the cheapest key
/// derivation the app offers) and files of a few dozen bytes, so a case costs one key derivation per
/// backup operation and nothing else.
/// </para>
/// <para>
/// Progress is pinned by invariant rather than by callback count: how many reports arrive and in what
/// order is scheduling-dependent, so a case only asserts that no report contradicts the totals the run
/// was started with and that the furthest progress ever announced is exactly what the result reports.
/// </para>
/// <para>
/// A missing chunk and a truncated chunk are deliberately separate cases. An absent chunk is an I/O
/// failure the engine classifies as file-level, so the run keeps going and salvages every file whose
/// chunks are intact; a chunk truncated to fewer bytes than its authentication tag cannot be
/// authenticated at all, which is a cryptographic failure that must abort the entire run rather than be
/// recorded as one more per-file problem. The update cases likewise pin behaviour that costs data on
/// purpose — a file that fails during an update is dropped from the new manifest and the chunk its
/// previous version referenced is then pruned, so the earlier copy is gone rather than retained — and
/// exist so that behaviour cannot change silently.
/// </para>
/// </remarks>
public sealed class ChunkedBackupFailurePathTests
{
    /// <summary>
    /// The password every backup in this fixture is created, updated, restored, and verified with;
    /// long and varied enough to clear the validator's strength warnings.
    /// </summary>
    private const string Password = "Correct-Horse-Battery-Staple-42";

    /// <summary>
    /// A second, unrelated password used to prove that an update under the wrong password cannot
    /// touch the archive it failed to open.
    /// </summary>
    private const string OtherPassword = "Wrong-Horse-Battery-Staple-99";

    [Test]
    public async Task Create_OneSourceFileVanishesBeforeItIsRead_IsolatesTheFailureAndKeepsProgressConsistent()
    {
        const string KeepAContent = "the first file, which must survive its neighbour's failure";
        const string KeepBContent = "the second file, nested, which must also survive";

        await using var provider = TestHost.CreateProvider();
        var service = provider.GetRequiredService<IChunkedBackupService>();

        using var source = new TempDir();
        using var archive = new TempDir();
        using var restored = new TempDir();

        var keepA = source.WriteText("keep-a.txt", KeepAContent);
        var keepB = source.WriteText(Path.Combine("dir", "keep-b.txt"), KeepBContent);
        var doomed = source.WriteText("doomed.txt", "removed after enumeration, before it is read");

        var survivingBytes = new FileInfo(keepA).Length + new FileInfo(keepB).Length;
        var totalBytes = survivingBytes + new FileInfo(doomed).Length;

        var recorded = new RecordingProgress<BackupStatus>();
        var result = await service.CreateAsync(
            source.Path,
            archive.Path,
            NewRequest(source.Path, archive.Path, BackupOperation.Create),
            new HookedProgress(recorded, () => File.Delete(doomed)),
            CancellationToken.None
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True, "One unreadable file must not sink the whole backup.");
            Assert.That(result.Value.IsSuccess, Is.False, "The result claimed success while a file had failed.");
            Assert.That(result.Value.TotalFiles, Is.EqualTo(3));
            Assert.That(result.Value.ProcessedFiles, Is.EqualTo(2));
            Assert.That(result.Value.TotalBytes, Is.EqualTo(totalBytes));
            Assert.That(result.Value.Errors, Has.Count.EqualTo(1));
            Assert.That(
                result.Value.Errors,
                Has.All.Matches<LocalizableMessage>(static e => e.Code == MessageCode.EncryptionErrorFormat)
            );
        }

        var reports = recorded.Reports;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reports, Is.Not.Empty, "The engine reported no progress at all.");
            Assert.That(reports[0].ProcessedFiles, Is.Zero, "The first report is the pre-loop baseline.");
            Assert.That(reports[0].ProcessedBytes, Is.Zero, "The first report is the pre-loop baseline.");
            Assert.That(
                reports,
                Has.All.Matches<BackupStatus>(r => r.TotalFiles == 3 && r.TotalBytes == totalBytes),
                "A progress report disagreed with the totals the run was started with."
            );
            Assert.That(
                reports.Max(static r => r.ProcessedFiles),
                Is.EqualTo(result.Value.ProcessedFiles),
                "Progress announced a different number of completed files than the result reports."
            );
            Assert.That(
                reports.Max(static r => r.ProcessedBytes),
                Is.EqualTo(survivingBytes),
                "Progress counted bytes for a file that never made it into the archive."
            );
        }

        var restoreResult = await service.RestoreAsync(
            archive.Path,
            restored.Path,
            NewRequest(archive.Path, restored.Path, BackupOperation.Restore),
            new HookedProgress(new RecordingProgress<BackupStatus>()),
            CancellationToken.None
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                restoreResult.IsSuccess && restoreResult.Value.IsSuccess,
                Is.True,
                "The archive written around a failed file did not restore cleanly."
            );
            Assert.That(File.ReadAllText(Path.Combine(restored.Path, "keep-a.txt")), Is.EqualTo(KeepAContent));
            Assert.That(
                File.ReadAllText(Path.Combine(restored.Path, "dir", "keep-b.txt")),
                Is.EqualTo(KeepBContent)
            );
            Assert.That(
                File.Exists(Path.Combine(restored.Path, "doomed.txt")),
                Is.False,
                "The file that failed was recorded in the manifest anyway."
            );
        }
    }

    [Test]
    public async Task Create_EverySourceFileFails_FailsWithAllFilesFailed()
    {
        await using var provider = TestHost.CreateProvider();
        var service = provider.GetRequiredService<IChunkedBackupService>();

        using var source = new TempDir();
        using var archive = new TempDir();

        var doomed = source.WriteText("only.txt", "the one and only file, removed before it is read");

        var result = await service.CreateAsync(
            source.Path,
            archive.Path,
            NewRequest(source.Path, archive.Path, BackupOperation.Create),
            new HookedProgress(new RecordingProgress<BackupStatus>(), () => File.Delete(doomed)),
            CancellationToken.None
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False, "A backup that captured no file at all was reported as a result.");
            Assert.That(
                CollectCodes(result),
                Is.EquivalentTo(new[] { MessageCode.AllFilesFailed, MessageCode.EncryptionErrorFormat }),
                "A total failure must name both the overall outcome and the file that caused it."
            );
        }
    }

    [Test]
    public async Task Create_EmptySourceDirectory_ReportsNoFilesAndLeavesTheDestinationEmpty()
    {
        await using var provider = TestHost.CreateProvider();
        var service = provider.GetRequiredService<IChunkedBackupService>();

        using var source = new TempDir();
        using var archive = new TempDir();

        var result = await service.CreateAsync(
            source.Path,
            archive.Path,
            NewRequest(source.Path, archive.Path, BackupOperation.Create),
            new HookedProgress(new RecordingProgress<BackupStatus>()),
            CancellationToken.None
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.IsSuccess, Is.False, "An empty source is not a successful backup.");
            Assert.That(result.Value.TotalFiles, Is.Zero);
            Assert.That(result.Value.ProcessedFiles, Is.Zero);
            Assert.That(result.Value.Errors, Has.Count.EqualTo(1));
            Assert.That(
                result.Value.Errors,
                Has.All.Matches<LocalizableMessage>(static e => e.Code == MessageCode.NoFilesInSourceDirectory)
            );

            Assert.That(
                Directory.GetFileSystemEntries(archive.Path),
                Is.Empty,
                "An empty source left something behind that looks like a backup."
            );
        }
    }

    [Test]
    public async Task CreateThenRestore_SourceHoldsOnlyEmptyFiles_StoresNoChunksAndRestoresZeroLengthFiles()
    {
        await using var provider = TestHost.CreateProvider();
        var service = provider.GetRequiredService<IChunkedBackupService>();

        using var source = new TempDir();
        using var archive = new TempDir();
        using var restored = new TempDir();

        _ = source.WriteFile("empty-root.dat", []);
        _ = source.WriteFile(Path.Combine("dir", "empty-nested.dat"), []);

        var createResult = await service.CreateAsync(
            source.Path,
            archive.Path,
            NewRequest(source.Path, archive.Path, BackupOperation.Create),
            new HookedProgress(new RecordingProgress<BackupStatus>()),
            CancellationToken.None
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(createResult.IsSuccess && createResult.Value.IsSuccess, Is.True, "Empty files broke the backup.");
            Assert.That(createResult.Value.TotalFiles, Is.EqualTo(2));
            Assert.That(createResult.Value.ProcessedFiles, Is.EqualTo(2));
            Assert.That(createResult.Value.TotalBytes, Is.Zero);

            Assert.That(
                ChunkFiles(archive.Path),
                Is.Empty,
                "A zero-length file was stored as a chunk, so the archive is no longer content-addressed."
            );
        }

        var restoreResult = await service.RestoreAsync(
            archive.Path,
            restored.Path,
            NewRequest(archive.Path, restored.Path, BackupOperation.Restore),
            new HookedProgress(new RecordingProgress<BackupStatus>()),
            CancellationToken.None
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                restoreResult.IsSuccess && restoreResult.Value.IsSuccess,
                Is.True,
                "An archive that holds no chunks failed to restore."
            );
            Assert.That(FilesUnder(restored.Path), Has.Length.EqualTo(2));
            Assert.That(new FileInfo(Path.Combine(restored.Path, "empty-root.dat")).Length, Is.Zero);
            Assert.That(new FileInfo(Path.Combine(restored.Path, "dir", "empty-nested.dat")).Length, Is.Zero);
        }
    }

    [Test]
    public async Task Restore_ChunkFileMissing_IsolatesTheFailureAndRestoresTheOtherFiles()
    {
        await using var provider = TestHost.CreateProvider();
        var service = provider.GetRequiredService<IChunkedBackupService>();

        using var source = new TempDir();
        using var archive = new TempDir();
        using var restored = new TempDir();

        var expected = BuildThreeFileTree(source);
        await CreateBackupAsync(service, source.Path, archive.Path);

        Assert.That(
            ChunkFiles(archive.Path),
            Has.Length.EqualTo(expected.Count),
            "The fixture assumes one chunk per file."
        );
        File.Delete(ChunkFiles(archive.Path)[0]);

        var result = await service.RestoreAsync(
            archive.Path,
            restored.Path,
            NewRequest(archive.Path, restored.Path, BackupOperation.Restore),
            new HookedProgress(new RecordingProgress<BackupStatus>()),
            CancellationToken.None
        );

        var reproduced = CountReproduced(expected, restored.Path);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True, "A missing chunk aborted the whole restore.");
            Assert.That(result.Value.IsSuccess, Is.False, "The restore claimed success with a chunk missing.");
            Assert.That(result.Value.TotalFiles, Is.EqualTo(expected.Count));
            Assert.That(result.Value.ProcessedFiles, Is.EqualTo(expected.Count - 1));
            Assert.That(result.Value.Errors, Has.Count.EqualTo(1));

            // A restore failure must not be reported as an encryption failure: the user is reading
            // files out, and being told the file could not be encrypted is simply wrong.
            Assert.That(
                result.Value.Errors,
                Has.All.Matches<LocalizableMessage>(static e => e.Code == MessageCode.DecryptionErrorFormat)
            );
            Assert.That(
                reproduced,
                Is.EqualTo(expected.Count - 1),
                "The files whose chunks were intact were not reproduced byte for byte."
            );
        }
    }

    [Test]
    public async Task Restore_ChunkFileTruncated_AbortsTheWholeRunAndReportsInvalidPassword()
    {
        await using var provider = TestHost.CreateProvider();
        var service = provider.GetRequiredService<IChunkedBackupService>();

        using var source = new TempDir();
        using var archive = new TempDir();
        using var restored = new TempDir();

        _ = BuildThreeFileTree(source);
        await CreateBackupAsync(service, source.Path, archive.Path);

        var chunkFile = ChunkFiles(archive.Path)[0];
        var intact = await File.ReadAllBytesAsync(chunkFile);
        await File.WriteAllBytesAsync(chunkFile, intact[..4]);

        var result = await service.RestoreAsync(
            archive.Path,
            restored.Path,
            NewRequest(archive.Path, restored.Path, BackupOperation.Restore),
            new HookedProgress(new RecordingProgress<BackupStatus>()),
            CancellationToken.None
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False, "A chunk that fails authentication did not abort the restore.");
            Assert.That(
                CollectCodes(result),
                Is.EquivalentTo(new[] { MessageCode.InvalidPassword }),
                "A chunk that fails authentication must surface as an authentication failure and nothing else."
            );
        }
    }

    [Test]
    public async Task Restore_DestinationAlreadyHoldsFiles_OverwritesBackedUpPathsAndLeavesOthersAlone()
    {
        const string RestoredA = "the archived content of a.txt";
        const string RestoredB = "the archived content of the nested b.txt";
        const string Unrelated = "a file that predates the restore and is not part of the archive";

        await using var provider = TestHost.CreateProvider();
        var service = provider.GetRequiredService<IChunkedBackupService>();

        using var source = new TempDir();
        using var archive = new TempDir();
        using var restored = new TempDir();

        _ = source.WriteText("a.txt", RestoredA);
        _ = source.WriteText(Path.Combine("dir", "b.txt"), RestoredB);
        await CreateBackupAsync(service, source.Path, archive.Path);

        _ = restored.WriteText("a.txt", "stale content that is considerably longer than what the archive holds");
        _ = restored.WriteText("unrelated.txt", Unrelated);

        var result = await service.RestoreAsync(
            archive.Path,
            restored.Path,
            NewRequest(archive.Path, restored.Path, BackupOperation.Restore),
            new HookedProgress(new RecordingProgress<BackupStatus>()),
            CancellationToken.None
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.IsSuccess && result.Value.IsSuccess,
                Is.True,
                "Restoring over an existing destination did not succeed."
            );
            Assert.That(
                File.ReadAllText(Path.Combine(restored.Path, "a.txt")),
                Is.EqualTo(RestoredA),
                "The stale file, deliberately longer than the archived copy, was not replaced with exactly that content."
            );
            Assert.That(File.ReadAllText(Path.Combine(restored.Path, "dir", "b.txt")), Is.EqualTo(RestoredB));
            Assert.That(
                File.ReadAllText(Path.Combine(restored.Path, "unrelated.txt")),
                Is.EqualTo(Unrelated),
                "The restore touched a file the archive knows nothing about."
            );
            Assert.That(FilesUnder(restored.Path), Has.Length.EqualTo(3));
        }
    }

    [Test]
    public async Task Update_ChangedFileVanishesBeforeItIsRead_IsolatesTheFailureAndDropsItsEntry()
    {
        const string StableContent = "a file that never changes";
        const string RevisedContent = "the revised content that does get re-chunked";

        await using var provider = TestHost.CreateProvider();
        var service = provider.GetRequiredService<IChunkedBackupService>();

        using var source = new TempDir();
        using var archive = new TempDir();
        using var restored = new TempDir();

        _ = source.WriteText("stable.txt", StableContent);
        _ = source.WriteText("revised.txt", "the original content of the file that gets revised");
        _ = source.WriteText("doomed.txt", "the original content of the file that disappears");

        await CreateBackupAsync(service, source.Path, archive.Path);

        _ = source.WriteText("revised.txt", RevisedContent);
        var doomed = source.WriteText("doomed.txt", "revised, but the file is gone before it is re-read");

        var result = await service.UpdateAsync(
            source.Path,
            archive.Path,
            NewRequest(source.Path, archive.Path, BackupOperation.Update),
            new HookedProgress(new RecordingProgress<BackupStatus>(), () => File.Delete(doomed)),
            CancellationToken.None
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True, "One unreadable file must not sink the whole update.");
            Assert.That(result.Value.IsSuccess, Is.False, "The update claimed success while a file had failed.");

            Assert.That(
                result.Value.TotalFiles,
                Is.EqualTo(2),
                "Only the two modified files are re-chunked; the untouched one is carried over."
            );
            Assert.That(result.Value.ProcessedFiles, Is.EqualTo(1));
            Assert.That(result.Value.Errors, Has.Count.EqualTo(1));
            Assert.That(
                result.Value.Errors,
                Has.All.Matches<LocalizableMessage>(static e => e.Code == MessageCode.EncryptionErrorFormat)
            );
        }

        var restoreResult = await service.RestoreAsync(
            archive.Path,
            restored.Path,
            NewRequest(archive.Path, restored.Path, BackupOperation.Restore),
            new HookedProgress(new RecordingProgress<BackupStatus>()),
            CancellationToken.None
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                restoreResult.IsSuccess && restoreResult.Value.IsSuccess,
                Is.True,
                "The archive left behind by a partially failed update is not restorable."
            );
            Assert.That(File.ReadAllText(Path.Combine(restored.Path, "stable.txt")), Is.EqualTo(StableContent));
            Assert.That(File.ReadAllText(Path.Combine(restored.Path, "revised.txt")), Is.EqualTo(RevisedContent));

            Assert.That(
                File.Exists(Path.Combine(restored.Path, "doomed.txt")),
                Is.False,
                "The failed file's entry survived the update; the manifest now points at a pruned chunk."
            );
            Assert.That(FilesUnder(restored.Path), Has.Length.EqualTo(2));
        }
    }

    [Test]
    public async Task Update_ChunksDirectoryRemovedBeforePruning_CompletesWithoutResurrectingIt()
    {
        await using var provider = TestHost.CreateProvider();
        var service = provider.GetRequiredService<IChunkedBackupService>();

        using var source = new TempDir();
        using var archive = new TempDir();

        _ = source.WriteText("a.txt", "content that is not touched between the backup and the update");
        _ = source.WriteText(Path.Combine("dir", "b.txt"), "nested content that is likewise untouched");

        await CreateBackupAsync(service, source.Path, archive.Path);

        var chunksDir = Path.Combine(archive.Path, BackupConstants.ChunksDirectoryName);
        var result = await service.UpdateAsync(
            source.Path,
            archive.Path,
            NewRequest(source.Path, archive.Path, BackupOperation.Update),
            new HookedProgress(
                new RecordingProgress<BackupStatus>(),
                () => Directory.Delete(chunksDir, recursive: true)
            ),
            CancellationToken.None
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.IsSuccess,
                Is.True,
                "A vanished chunks directory failed the update; pruning is best-effort cleanup run after the manifest."
            );
            Assert.That(result.Value.IsSuccess, Is.True);
            Assert.That(result.Value.TotalFiles, Is.Zero, "Nothing changed, so nothing should have been re-chunked.");
            Assert.That(result.Value.Errors, Is.Empty);
            Assert.That(Directory.Exists(chunksDir), Is.False, "Pruning recreated the chunks directory.");
            Assert.That(
                File.Exists(Path.Combine(archive.Path, BackupConstants.ManifestFileName)),
                Is.True,
                "The update did not leave a manifest behind."
            );
        }
    }

    [Test]
    public async Task Update_DestinationHasNoManifest_FailsWithManifestRequiredForUpdate()
    {
        await using var provider = TestHost.CreateProvider();
        var service = provider.GetRequiredService<IChunkedBackupService>();

        using var source = new TempDir();
        using var archive = new TempDir();

        _ = source.WriteText("a.txt", "content that must not be written anywhere");
        _ = archive.WriteText("stray.txt", "not a manifest");

        var result = await service.UpdateAsync(
            source.Path,
            archive.Path,
            NewRequest(source.Path, archive.Path, BackupOperation.Update),
            new HookedProgress(new RecordingProgress<BackupStatus>()),
            CancellationToken.None
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False, "An update without a manifest was accepted.");
            Assert.That(result.Errors, Has.Count.EqualTo(1));
            Assert.That(
                result.Errors,
                Has.All.Matches<LocalizableMessage>(static e => e.Code == MessageCode.ManifestRequiredForUpdate)
            );
            Assert.That(
                File.Exists(Path.Combine(archive.Path, BackupConstants.ManifestFileName)),
                Is.False,
                "The refused update wrote a manifest anyway."
            );
            Assert.That(FilesUnder(archive.Path), Has.Length.EqualTo(1), "The refused update wrote to the destination.");
        }
    }

    [Test]
    public async Task Update_WrongPassword_FailsWithInvalidPasswordAndLeavesTheArchiveUnchanged()
    {
        await using var provider = TestHost.CreateProvider();
        var service = provider.GetRequiredService<IChunkedBackupService>();

        using var source = new TempDir();
        using var archive = new TempDir();

        _ = source.WriteText("a.txt", "the archived content");
        _ = source.WriteText(Path.Combine("dir", "b.txt"), "the nested archived content");
        await CreateBackupAsync(service, source.Path, archive.Path);

        _ = source.WriteText("a.txt", "content revised by someone who does not know the password");
        var archiveBefore = Snapshot(archive.Path);

        var result = await service.UpdateAsync(
            source.Path,
            archive.Path,
            NewRequest(source.Path, archive.Path, BackupOperation.Update, OtherPassword),
            new HookedProgress(new RecordingProgress<BackupStatus>()),
            CancellationToken.None
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False, "An update under the wrong password succeeded.");
            Assert.That(result.Errors, Has.Count.EqualTo(1));
            Assert.That(
                result.Errors,
                Has.All.Matches<LocalizableMessage>(static e => e.Code == MessageCode.InvalidPassword)
            );

            Assert.That(
                Snapshot(archive.Path),
                Is.EqualTo(archiveBefore),
                "An update that could not open the manifest still modified the archive."
            );
        }
    }

    [Test]
    public async Task Create_CancelledBeforeAnyFileIsProcessed_StopsAndWritesNoManifest()
    {
        await using var provider = TestHost.CreateProvider();
        var service = provider.GetRequiredService<IChunkedBackupService>();

        using var source = new TempDir();
        using var archive = new TempDir();

        _ = source.WriteText("a.txt", "alpha payload");
        _ = source.WriteText(Path.Combine("dir", "b.txt"), "bravo payload");

        using var cts = new CancellationTokenSource();
        var request = NewRequest(source.Path, archive.Path, BackupOperation.Create);
        var progress = new HookedProgress(new RecordingProgress<BackupStatus>(), cts.Cancel);

        _ = Assert.CatchAsync<OperationCanceledException>(
            () => service.CreateAsync(source.Path, archive.Path, request, progress, cts.Token)
        );

        Assert.That(
            FilesUnder(archive.Path),
            Is.Empty,
            "A cancelled backup left files behind; a manifest there would describe an archive holding no files at all."
        );
    }

    [TestCase(BackupOperation.Update)]
    [TestCase(BackupOperation.Restore)]
    [TestCase(BackupOperation.Verify)]
    public async Task ExistingBackup_CancelledBeforeAnyFileIsProcessed_StopsAndLeavesTheArchiveIntact(
        BackupOperation operation
    )
    {
        await using var provider = TestHost.CreateProvider();
        var service = provider.GetRequiredService<IChunkedBackupService>();

        using var source = new TempDir();
        using var archive = new TempDir();
        using var restored = new TempDir();

        _ = source.WriteText("a.txt", "alpha payload");
        _ = source.WriteText(Path.Combine("dir", "b.txt"), "bravo payload");
        await CreateBackupAsync(service, source.Path, archive.Path);

        _ = source.WriteText("a.txt", "alpha payload, revised so the update has work to do");
        var archiveBefore = Snapshot(archive.Path);

        var (operationSource, operationDestination) = operation switch
        {
            BackupOperation.Update => (source.Path, archive.Path),
            BackupOperation.Restore => (archive.Path, restored.Path),
            _ => (archive.Path, string.Empty),
        };

        using var cts = new CancellationTokenSource();
        var request = NewRequest(operationSource, operationDestination, operation);
        var progress = new HookedProgress(new RecordingProgress<BackupStatus>(), cts.Cancel);

        Task RunOperationAsync()
        {
            return operation switch
            {
                BackupOperation.Update => service.UpdateAsync(
                    operationSource,
                    operationDestination,
                    request,
                    progress,
                    cts.Token
                ),
                BackupOperation.Restore => service.RestoreAsync(
                    operationSource,
                    operationDestination,
                    request,
                    progress,
                    cts.Token
                ),
                _ => service.VerifyAsync(operationSource, request, progress, cts.Token),
            };
        }

        _ = Assert.CatchAsync<OperationCanceledException>(RunOperationAsync);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                Snapshot(archive.Path),
                Is.EqualTo(archiveBefore),
                "A cancelled operation modified the archive instead of stopping."
            );
            Assert.That(FilesUnder(restored.Path), Is.Empty, "A cancelled operation left restored files behind.");
        }
    }

    /// <summary>
    /// Builds an AES plus PBKDF2 request without compression that proceeds past advisory warnings.
    /// PBKDF2 is the cheapest key derivation the app offers, which keeps these fixtures affordable.
    /// </summary>
    /// <param name="sourcePath">The tree to back up, or the archive to read from.</param>
    /// <param name="destinationPath">The directory the archive or the restored files are written to.</param>
    /// <param name="operation">The operation the request describes.</param>
    /// <param name="password">The password to derive keys from; defaults to the fixture password.</param>
    /// <returns>The assembled request.</returns>
    private static BackupRequest NewRequest(
        string sourcePath,
        string destinationPath,
        BackupOperation operation,
        string password = Password
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
            CompressionMode.None,
            ProceedOnWarnings: true
        );
    }

    /// <summary>
    /// Creates a backup through the real engine and fails the test if it did not fully succeed, so a
    /// broken fixture never masquerades as a failure-handling defect.
    /// </summary>
    /// <param name="service">The chunked backup service under test.</param>
    /// <param name="sourcePath">The directory to back up.</param>
    /// <param name="archivePath">The directory the backup is written to.</param>
    /// <returns>A task that completes once the backup exists.</returns>
    private static async Task CreateBackupAsync(
        IChunkedBackupService service,
        string sourcePath,
        string archivePath
    )
    {
        var result = await service.CreateAsync(
            sourcePath,
            archivePath,
            NewRequest(sourcePath, archivePath, BackupOperation.Create),
            new HookedProgress(new RecordingProgress<BackupStatus>()),
            CancellationToken.None
        );

        Assert.That(
            result.IsSuccess && result.Value.IsSuccess,
            Is.True,
            "The fixture could not create the backup under test."
        );
    }

    /// <summary>
    /// Writes three files of distinct, short content, one of them nested, so the resulting archive
    /// holds exactly one chunk per file.
    /// </summary>
    /// <remarks>
    /// Every file is well under the chunker's minimum chunk size and holds content no other file shares,
    /// so damaging a single stored chunk can only ever break one manifest entry.
    /// </remarks>
    /// <param name="source">The directory the files are written into.</param>
    /// <returns>The written content keyed by path relative to <paramref name="source"/>.</returns>
    private static Dictionary<string, string> BuildThreeFileTree(TempDir source)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["alpha.txt"] = "alpha content, stored as a single chunk",
            [Path.Combine("dir", "bravo.txt")] = "bravo content, stored as a single chunk",
            ["charlie.txt"] = "charlie content, stored as a single chunk",
        };

        foreach (var (relativePath, content) in files)
        {
            _ = source.WriteText(relativePath, content);
        }

        return files;
    }

    /// <summary>
    /// Counts how many of the expected files were reproduced under a restore root with exactly their
    /// original content.
    /// </summary>
    /// <param name="expected">The original content keyed by relative path.</param>
    /// <param name="restoredRoot">The directory the archive was restored into.</param>
    /// <returns>The number of files that match their original byte for byte.</returns>
    private static int CountReproduced(Dictionary<string, string> expected, string restoredRoot)
    {
        return expected.Count(pair =>
            File.Exists(Path.Combine(restoredRoot, pair.Key))
            && string.Equals(
                File.ReadAllText(Path.Combine(restoredRoot, pair.Key)),
                pair.Value,
                StringComparison.Ordinal
            )
        );
    }

    /// <summary>
    /// Lists the encrypted chunk files stored under an archive root, in a stable order so a test
    /// always damages the same chunk.
    /// </summary>
    /// <param name="archiveRoot">The directory a backup was written to.</param>
    /// <returns>The absolute paths of the stored chunk files, ordered by name.</returns>
    private static string[] ChunkFiles(string archiveRoot)
    {
        return
        [
            .. Directory
                .GetFiles(
                    Path.Combine(archiveRoot, BackupConstants.ChunksDirectoryName),
                    "*" + BackupConstants.AppFileExtension
                )
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// Lists every file under a directory, treating a directory that was never created as empty.
    /// </summary>
    /// <param name="root">The directory to enumerate.</param>
    /// <returns>The absolute paths found beneath <paramref name="root"/>.</returns>
    private static string[] FilesUnder(string root)
    {
        return Directory.Exists(root) ? Directory.GetFiles(root, "*", SearchOption.AllDirectories) : [];
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
            .. FilesUnder(root)
                .Select(file =>
                    Path.GetRelativePath(root, file).Replace('\\', '/')
                    + "|"
                    + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)))
                )
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// Gathers the engine's own error codes together with the per-file codes of the inner backup
    /// result, which is only read when the outer result succeeded because
    /// <see cref="BackupZCrypt.Application.ValueObjects.Result{T}.Value"/> throws on a failure.
    /// </summary>
    /// <param name="result">The outcome to inspect.</param>
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

    /// <summary>
    /// A progress sink that serializes the reports it forwards to <paramref name="inner"/> and runs a
    /// one-shot hook the first time the engine reports anything.
    /// </summary>
    /// <remarks>
    /// Every operation emits one baseline report before it starts processing files, so the hook runs
    /// at a point where no file has been opened yet. That is what lets a test delete a file, remove a
    /// directory, or cancel a token and know exactly what the engine will see next, without a timer or
    /// a sleep. Forwarding under the lock also keeps the recorded list consistent while the engine
    /// reports from several worker threads at once.
    /// </remarks>
    /// <param name="inner">The sink the reports are forwarded to.</param>
    /// <param name="onFirstReport">The action to run once, on the first report; omitted to only record.</param>
    private sealed class HookedProgress(IProgress<BackupStatus> inner, Action? onFirstReport = null)
        : IProgress<BackupStatus>
    {
        /// <summary>
        /// Serializes both the hook and the forwarded reports.
        /// </summary>
        private readonly Lock _gate = new();

        /// <summary>
        /// Whether the one-shot hook has already run.
        /// </summary>
        private bool _hookFired;

        /// <inheritdoc/>
        public void Report(BackupStatus value)
        {
            lock (_gate)
            {
                if (!_hookFired)
                {
                    _hookFired = true;
                    onFirstReport?.Invoke();
                }

                inner.Report(value);
            }
        }
    }
}
