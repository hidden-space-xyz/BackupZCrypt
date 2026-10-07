using BackupZCrypt.Application.Orchestrators;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.Utilities.Helpers;
using BackupZCrypt.Application.Validators.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Application.ValueObjects.Backup;
using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Services.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Backup;
using BackupZCrypt.Domain.ValueObjects.Localization;
using BackupZCrypt.Test.Common;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BackupZCrypt.Test.Unit.Application;

/// <summary>
/// Unit tests for the backup operation runner: the validation gate, the preview of an update or
/// restore, the post-validation source and destination checks, the backup lock, operation dispatch,
/// cancellation, and unexpected-error mapping. The validator, file system, storage, and backup service
/// are all substituted, so nothing here touches the real disk.
/// </summary>
/// <remarks>
/// The runner must never delete user data. A create used to empty an existing destination before it
/// started; the assertions that no deletion was received guard against that coming back.
/// </remarks>
public sealed class BackupOperationRunnerTests
{
    /// <summary>
    /// The rooted source path the requests point at. Nothing is created on disk because the file
    /// system is substituted, but the path must be absolute to survive path normalization.
    /// </summary>
    private static readonly string SourceDir = Path.GetFullPath(
        Path.Join(Path.GetTempPath(), "bzc-runner-src")
    );

    /// <summary>
    /// The rooted destination path the requests point at, kept distinct from <see cref="SourceDir"/>.
    /// </summary>
    private static readonly string DestinationDir = Path.GetFullPath(
        Path.Join(Path.GetTempPath(), "bzc-runner-dst")
    );

    /// <summary>
    /// The substituted validator, which lets a test choose exactly which blocking errors and advisory
    /// warnings the runner sees without depending on the real validator's file-system probes.
    /// </summary>
    private readonly IBackupRequestValidator validator =
        Substitute.For<IBackupRequestValidator>();

    /// <summary>
    /// The substituted file system the runner probes for the source and destination.
    /// </summary>
    private readonly IFileOperationsService fileOperations =
        Substitute.For<IFileOperationsService>();

    /// <summary>
    /// The substituted backup engine the runner dispatches each operation to.
    /// </summary>
    private readonly IChunkedBackupService chunkedBackupService =
        Substitute.For<IChunkedBackupService>();

    /// <summary>
    /// The substituted storage service the preview warnings query for free space.
    /// </summary>
    private readonly ISystemStorageService systemStorage = Substitute.For<ISystemStorageService>();

    /// <summary>
    /// The progress sink handed to the runner; assertions check it is forwarded unchanged.
    /// </summary>
    private readonly RecordingProgress<BackupStatus> progress = new();

    /// <summary>
    /// Creates a runner wired to the substituted dependencies.
    /// </summary>
    /// <returns>The system under test.</returns>
    private BackupOperationRunner CreateSut()
    {
        return new(this.validator, this.fileOperations, this.chunkedBackupService, this.systemStorage);
    }

    /// <summary>
    /// Builds a request whose fields are individually valid so a test only varies what it exercises.
    /// </summary>
    /// <param name="operation">The operation the request asks for.</param>
    /// <param name="proceedOnWarnings">Whether the user agreed to proceed past advisory warnings.</param>
    /// <param name="source">The source path; defaults to <see cref="SourceDir"/>.</param>
    /// <param name="destination">The destination path; defaults to <see cref="DestinationDir"/>.</param>
    /// <param name="password">The password, also used as the confirmation so the two always match.</param>
    /// <returns>A request built from the supplied values.</returns>
    private static BackupRequest Request(
        BackupOperation operation,
        bool proceedOnWarnings = false,
        string? source = null,
        string? destination = null,
        string password = "Correct-Horse-Battery-Staple-42"
    )
    {
        return new(
            source ?? SourceDir,
            destination ?? DestinationDir,
            password,
            password,
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.PBKDF2,
            operation,
            CompressionMode.None,
            proceedOnWarnings
        );
    }

    /// <summary>
    /// Appends a redundant <c>.</c> segment so the raw path differs from its normalized form, letting
    /// a test prove the runner hands the resolved path to the backup service.
    /// </summary>
    /// <param name="path">The already-normalized path to perturb.</param>
    /// <returns>A path that normalizes back to <paramref name="path"/>.</returns>
    private static string Unnormalized(string path)
    {
        return path + Path.DirectorySeparatorChar + ".";
    }

    /// <summary>
    /// Builds a path the running platform cannot resolve to an absolute form.
    /// </summary>
    /// <returns>A raw path that fails normalization.</returns>
    private static string UnnormalizablePath()
    {
        return OperatingSystem.IsWindows() ? new string('a', 300_000) : "some-folder\0name";
    }

    /// <summary>
    /// Projects messages down to their codes so assertions ignore format arguments.
    /// </summary>
    /// <param name="messages">The errors or warnings to project.</param>
    /// <returns>The code of each message, in the order reported.</returns>
    private static List<MessageCode> Codes(IReadOnlyList<LocalizableMessage> messages)
    {
        return [.. messages.Select(m => m.Code)];
    }

    /// <summary>
    /// Builds the successful outcome the substituted backup service reports.
    /// </summary>
    /// <returns>A successful result carrying a successful <see cref="BackupResult"/>.</returns>
    private static Result<BackupResult> SuccessResult()
    {
        return Result<BackupResult>.Success(new BackupResult(TimeSpan.Zero, 0, 0, 0));
    }

    /// <summary>
    /// Builds the validator's return shape from bare codes, since these tests only assert on codes.
    /// </summary>
    /// <param name="codes">The codes the substituted validator should report.</param>
    /// <returns>One message per code, in the order given.</returns>
    private static List<LocalizableMessage> Messages(params MessageCode[] codes)
    {
        return [.. codes.Select(code => new LocalizableMessage(code))];
    }

    /// <summary>
    /// Makes the substituted validator report neither errors nor warnings.
    /// </summary>
    private void PassValidation()
    {
        _ = this.validator
            .AnalyzeErrorsAsync(Arg.Any<BackupRequest>(), Arg.Any<CancellationToken>())
            .Returns(Messages());
        _ = this.validator
            .AnalyzeWarningsAsync(Arg.Any<BackupRequest>(), Arg.Any<CancellationToken>())
            .Returns(Messages());
    }

    /// <summary>
    /// Makes the update and restore previews report a backup the source fully matches.
    /// </summary>
    /// <param name="removedPaths">The recorded files the update preview reports as removed.</param>
    /// <param name="matchedFiles">The recorded files the update preview reports as still present.</param>
    /// <param name="totalBytes">The recorded size the restore preview reports.</param>
    private void StubPreviews(string[]? removedPaths = null, int matchedFiles = 3, long totalBytes = 10)
    {
        _ = this.chunkedBackupService
            .PreviewUpdateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result<BackupPreview>.Success(
                    new BackupPreview(3, totalBytes, matchedFiles, removedPaths ?? [], 0)
                )
            );
        _ = this.chunkedBackupService
            .PreviewRestoreAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<BackupPreview>.Success(new BackupPreview(3, totalBytes, 3, [], 0)));
    }

    /// <summary>
    /// Makes every backup service operation report success, so a test can assert on which one ran
    /// rather than on what it returned.
    /// </summary>
    private void StubOperationsSucceed()
    {
        this.StubPreviews();
        _ = this.chunkedBackupService
            .CreateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(SuccessResult());
        _ = this.chunkedBackupService
            .UpdateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(SuccessResult());
        _ = this.chunkedBackupService
            .RestoreAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(SuccessResult());
    }

    /// <summary>
    /// Asserts that the runner never asked the file system to delete anything.
    /// </summary>
    private void AssertNothingDeleted()
    {
        this.fileOperations.DidNotReceive().DeleteFile(Arg.Any<string>());
        this.fileOperations.DidNotReceive().DeleteEmptyDirectory(Arg.Any<string>());
    }

    [Fact]
    internal async Task RunAsync_ValidationErrors_FailsAndNeverStartsTheBackup()
    {
        _ = this.validator
            .AnalyzeErrorsAsync(Arg.Any<BackupRequest>(), Arg.Any<CancellationToken>())
            .Returns(Messages(MessageCode.PasswordTooShort));
        _ = this.fileOperations.DirectoryExists(Arg.Any<string>()).Returns(true);

        var result = await this.CreateSut()
            .RunAsync(Request(BackupOperation.Create), this.progress, CancellationToken.None);

        Assert.Multiple(
            () => Assert.False(result.IsSuccess),
            () =>
                Assert.Equal<MessageCode>(
                    [MessageCode.PasswordTooShort],
                    Codes(result.Errors)
                )
        );

        await this.validator.DidNotReceive()
            .AnalyzeWarningsAsync(Arg.Any<BackupRequest>(), Arg.Any<CancellationToken>());
        await this.fileOperations.DidNotReceive()
            .CreateDirectoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        this.AssertNothingDeleted();
        await this.chunkedBackupService.DidNotReceive()
            .CreateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    internal async Task RunAsync_WarningsRaised_RunsOnlyWhenTheUserAgreedToProceed(
        bool proceedOnWarnings
    )
    {
        _ = this.validator
            .AnalyzeErrorsAsync(Arg.Any<BackupRequest>(), Arg.Any<CancellationToken>())
            .Returns(Messages());
        _ = this.validator
            .AnalyzeWarningsAsync(Arg.Any<BackupRequest>(), Arg.Any<CancellationToken>())
            .Returns(Messages(MessageCode.DestinationContainsBackup));
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations.DirectoryExists(DestinationDir).Returns(true);
        this.StubOperationsSucceed();

        var request = Request(BackupOperation.Create, proceedOnWarnings: proceedOnWarnings);

        var result = await this.CreateSut().RunAsync(request, this.progress, CancellationToken.None);

        Assert.Multiple(
            () => Assert.True(result.IsSuccess),
            () => Assert.Equal(!proceedOnWarnings, result.Value.NeedsWarningConfirmation)
        );

        if (proceedOnWarnings)
        {
            Assert.Multiple(
                () => Assert.True(result.Value.Completion!.IsSuccess),
                () => Assert.Empty(result.Value.PendingWarnings)
            );
        }
        else
        {
            Assert.Multiple(
                () => Assert.Null(result.Value.Completion),
                () =>
                    Assert.Equal<MessageCode>(
                        [MessageCode.DestinationContainsBackup],
                        Codes(result.Value.PendingWarnings)
                    )
            );
        }

        await this.chunkedBackupService.Received(proceedOnWarnings ? 1 : 0)
            .CreateAsync(SourceDir, DestinationDir, request, this.progress, Arg.Any<CancellationToken>());
        this.AssertNothingDeleted();
    }

    [Theory]
    [InlineData(BackupOperation.Create, true)]
    [InlineData(BackupOperation.Create, false)]
    [InlineData(BackupOperation.Update, true)]
    [InlineData(BackupOperation.Restore, true)]
    [InlineData(BackupOperation.Restore, false)]
    internal async Task RunAsync_SuccessfulOperation_NeverDeletesAnythingAndOnlyACreatePreparesTheDestination(
        BackupOperation operation,
        bool destinationExists
    )
    {
        this.PassValidation();
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations.DirectoryExists(DestinationDir).Returns(destinationExists);
        this.StubOperationsSucceed();

        var result = await this.CreateSut()
            .RunAsync(Request(operation), this.progress, CancellationToken.None);

        Assert.True(result.Value.Completion!.IsSuccess);

        await this.fileOperations.Received(operation is BackupOperation.Create ? 1 : 0)
            .CreateDirectoryAsync(DestinationDir, Arg.Any<CancellationToken>());
        this.AssertNothingDeleted();
    }

    [Theory]
    [InlineData(BackupOperation.Create)]
    [InlineData(BackupOperation.Update)]
    internal async Task RunAsync_WritingOperation_HoldsTheBackupLockForTheWholeRun(BackupOperation operation)
    {
        this.PassValidation();
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations.DirectoryExists(DestinationDir).Returns(true);
        _ = this.fileOperations
            .CombinePath(DestinationDir, BackupConstants.LockFileName)
            .Returns("lock-file");
        var backupLock = Substitute.For<IDisposable>();
        _ = this.fileOperations.AcquireExclusiveLock("lock-file").Returns(backupLock);
        this.StubOperationsSucceed();

        _ = await this.CreateSut().RunAsync(Request(operation), this.progress, CancellationToken.None);

        Received.InOrder(() =>
        {
            _ = this.fileOperations.AcquireExclusiveLock("lock-file");
            backupLock.Dispose();
        });
    }

    [Theory]
    [InlineData(BackupOperation.Create)]
    [InlineData(BackupOperation.Update)]
    internal async Task RunAsync_BackupLockedByAnotherOperation_ReportsBackupInUseWithoutRunning(
        BackupOperation operation
    )
    {
        this.PassValidation();
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations.DirectoryExists(DestinationDir).Returns(true);
        _ = this.fileOperations
            .AcquireExclusiveLock(Arg.Any<string>())
            .Throws(new IOException("in use"));
        this.StubOperationsSucceed();

        var result = await this.CreateSut().RunAsync(Request(operation), this.progress, CancellationToken.None);

        Assert.Equal<MessageCode>([MessageCode.BackupInUse], Codes(result.Errors));
        await this.chunkedBackupService.DidNotReceive()
            .CreateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            );
        await this.chunkedBackupService.DidNotReceive()
            .UpdateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    internal async Task RunAsync_RestoreWhileTheBackupIsBeingModified_ReportsBackupInUse()
    {
        this.PassValidation();
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations.IsLockHeld(Arg.Any<string>()).Returns(true);
        this.StubOperationsSucceed();

        var result = await this.CreateSut()
            .RunAsync(Request(BackupOperation.Restore), this.progress, CancellationToken.None);

        Assert.Equal<MessageCode>([MessageCode.BackupInUse], Codes(result.Errors));
        await this.chunkedBackupService.DidNotReceive()
            .RestoreAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    internal async Task RunAsync_FailedCreateIntoAFolderItCreated_RemovesTheFolderAgainWhenEmpty()
    {
        this.PassValidation();
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations.DirectoryExists(DestinationDir).Returns(false, true);
        _ = this.fileOperations.GetDirectoryEntryNames(DestinationDir).Returns([]);
        this.StubPreviews();
        _ = this.chunkedBackupService
            .CreateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result<BackupResult>.Failure(MessageCode.AllFilesFailed));

        var result = await this.CreateSut()
            .RunAsync(Request(BackupOperation.Create), this.progress, CancellationToken.None);

        Assert.False(result.IsSuccess);
        this.fileOperations.Received(1).DeleteEmptyDirectory(DestinationDir);
    }

    [Fact]
    internal async Task RunAsync_FailedCreateIntoAnExistingFolder_LeavesTheFolderAlone()
    {
        this.PassValidation();
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations.DirectoryExists(DestinationDir).Returns(true);
        _ = this.fileOperations.GetDirectoryEntryNames(DestinationDir).Returns([]);
        this.StubPreviews();
        _ = this.chunkedBackupService
            .CreateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result<BackupResult>.Failure(MessageCode.AllFilesFailed));

        _ = await this.CreateSut()
            .RunAsync(Request(BackupOperation.Create), this.progress, CancellationToken.None);

        this.AssertNothingDeleted();
    }

    [Fact]
    internal async Task RunAsync_UpdateThatRemovesFiles_AsksForConfirmationNamingThem()
    {
        this.PassValidation();
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations.DirectoryExists(DestinationDir).Returns(true);
        this.StubOperationsSucceed();
        this.StubPreviews(removedPaths: ["gone.txt"], matchedFiles: 2);

        var result = await this.CreateSut()
            .RunAsync(Request(BackupOperation.Update), this.progress, CancellationToken.None);

        Assert.Multiple(
            () => Assert.True(result.Value.NeedsWarningConfirmation),
            () =>
                Assert.Equal<MessageCode>(
                    [MessageCode.UpdateRemovesFilesFormat],
                    Codes(result.Value.PendingWarnings)
                ),
            () => Assert.Equal<object>([1, "gone.txt"], result.Value.PendingWarnings[0].Args)
        );
        await this.chunkedBackupService.DidNotReceive()
            .UpdateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    internal async Task RunAsync_UpdateFromASourceSharingNothingWithTheBackup_WarnsItWouldReplaceEverything()
    {
        this.PassValidation();
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations.DirectoryExists(DestinationDir).Returns(true);
        this.StubOperationsSucceed();
        this.StubPreviews(removedPaths: ["a.txt", "b.txt", "c.txt"], matchedFiles: 0);

        var result = await this.CreateSut()
            .RunAsync(Request(BackupOperation.Update), this.progress, CancellationToken.None);

        Assert.Equal<MessageCode>(
            [MessageCode.UpdateSourceMismatchFormat],
            Codes(result.Value.PendingWarnings)
        );
    }

    [Fact]
    internal async Task RunAsync_UpdateConfirmed_SkipsThePreviewAndRuns()
    {
        this.PassValidation();
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations.DirectoryExists(DestinationDir).Returns(true);
        this.StubOperationsSucceed();

        var result = await this.CreateSut()
            .RunAsync(
                Request(BackupOperation.Update, proceedOnWarnings: true),
                this.progress,
                CancellationToken.None
            );

        Assert.True(result.Value.Completion!.IsSuccess);
        await this.chunkedBackupService.DidNotReceive()
            .PreviewUpdateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
        await this.validator.DidNotReceive()
            .AnalyzeWarningsAsync(Arg.Any<BackupRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    internal async Task RunAsync_RestoreWithAWrongPassword_FailsBeforeCreatingTheDestination()
    {
        this.PassValidation();
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        this.StubOperationsSucceed();
        _ = this.chunkedBackupService
            .PreviewRestoreAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<BackupPreview>.Failure(MessageCode.InvalidPassword));

        var result = await this.CreateSut()
            .RunAsync(Request(BackupOperation.Restore), this.progress, CancellationToken.None);

        Assert.Equal<MessageCode>([MessageCode.InvalidPassword], Codes(result.Errors));
        await this.fileOperations.DidNotReceive()
            .CreateDirectoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await this.chunkedBackupService.DidNotReceive()
            .RestoreAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    internal async Task RunAsync_RestoreLargerThanTheFreeSpace_WarnsWithTheRecordedSize()
    {
        this.PassValidation();
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        this.StubOperationsSucceed();
        this.StubPreviews(totalBytes: 5_000);
        _ = this.systemStorage.GetPathRoot(Arg.Any<string>()).Returns("root");
        _ = this.systemStorage.IsDriveReady("root").Returns(true);
        _ = this.systemStorage.GetAvailableFreeSpace("root").Returns(1_000);

        var result = await this.CreateSut()
            .RunAsync(Request(BackupOperation.Restore), this.progress, CancellationToken.None);

        Assert.Equal<MessageCode>([MessageCode.LowDiskSpaceFormat], Codes(result.Value.PendingWarnings));
    }

    [Theory]
    [InlineData(BackupOperation.Create)]
    [InlineData(BackupOperation.Update)]
    [InlineData(BackupOperation.Restore)]
    internal async Task RunAsync_KnownOperation_ForwardsNormalizedPathsAndTokenToItsOwnMethod(
        BackupOperation operation
    )
    {
        this.PassValidation();
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations.DirectoryExists(DestinationDir).Returns(true);
        this.StubOperationsSucceed();

        using var cancellation = new CancellationTokenSource();
        var request = Request(
            operation,
            source: Unnormalized(SourceDir),
            destination: Unnormalized(DestinationDir)
        );

        var result = await this.CreateSut().RunAsync(request, this.progress, cancellation.Token);

        Assert.True(result.Value.Completion!.IsSuccess);

        await this.chunkedBackupService.Received(operation is BackupOperation.Create ? 1 : 0)
            .CreateAsync(SourceDir, DestinationDir, request, this.progress, cancellation.Token);
        await this.chunkedBackupService.Received(operation is BackupOperation.Update ? 1 : 0)
            .UpdateAsync(SourceDir, DestinationDir, request, this.progress, cancellation.Token);
        await this.chunkedBackupService.Received(operation is BackupOperation.Restore ? 1 : 0)
            .RestoreAsync(SourceDir, DestinationDir, request, this.progress, cancellation.Token);
        await this.chunkedBackupService.DidNotReceive()
            .VerifyAsync(
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    internal async Task RunAsync_NoProgressSink_ForwardsTheSharedNullSinkToTheEngine()
    {
        this.PassValidation();
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations.DirectoryExists(DestinationDir).Returns(true);
        this.StubOperationsSucceed();

        _ = await this.CreateSut()
            .RunAsync(Request(BackupOperation.Create), null, CancellationToken.None);

        await this.chunkedBackupService.Received(1)
            .CreateAsync(
                SourceDir,
                DestinationDir,
                Arg.Any<BackupRequest>(),
                NullProgress<BackupStatus>.Instance,
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData(true, MessageCode.SourceMustBeDirectory)]
    [InlineData(false, MessageCode.SourcePathNotExist)]
    internal async Task RunAsync_SourceIsAFileOrGone_FailsWithoutPreparingTheDestination(
        bool sourceIsFile,
        MessageCode expectedCode
    )
    {
        this.PassValidation();
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(false);
        _ = this.fileOperations.DirectoryExists(DestinationDir).Returns(true);
        _ = this.fileOperations.FileExists(SourceDir).Returns(sourceIsFile);

        var result = await this.CreateSut()
            .RunAsync(Request(BackupOperation.Create), this.progress, CancellationToken.None);

        Assert.Multiple(
            () => Assert.False(result.IsSuccess),
            () => Assert.Equal<MessageCode>([expectedCode], Codes(result.Errors))
        );

        await this.fileOperations.DidNotReceive()
            .CreateDirectoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        this.AssertNothingDeleted();
    }

    [Fact]
    internal async Task RunAsync_UpdateWithMissingDestination_FailsWithoutCreatingADecoyDirectory()
    {
        this.PassValidation();
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations.DirectoryExists(DestinationDir).Returns(false);
        this.StubOperationsSucceed();

        var result = await this.CreateSut()
            .RunAsync(
                Request(BackupOperation.Update, proceedOnWarnings: true),
                this.progress,
                CancellationToken.None
            );

        Assert.Multiple(
            () => Assert.False(result.IsSuccess),
            () =>
                Assert.Equal<MessageCode>(
                    [MessageCode.BackupDestinationMustExist],
                    Codes(result.Errors)
                )
        );

        await this.fileOperations.DidNotReceive()
            .CreateDirectoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await this.chunkedBackupService.DidNotReceive()
            .UpdateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    internal async Task RunAsync_BackupServiceThrows_ReportsUnexpectedErrorCarryingOnlyTheMessage()
    {
        this.PassValidation();
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.chunkedBackupService
            .CreateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(new InvalidOperationException("boom"));

        var result = await this.CreateSut()
            .RunAsync(Request(BackupOperation.Create), this.progress, CancellationToken.None);

        Assert.Multiple(
            () => Assert.False(result.IsSuccess),
            () =>
                Assert.Equal<MessageCode>(
                    [MessageCode.UnexpectedErrorFormat],
                    Codes(result.Errors)
                ),
            () => Assert.Equal<object>(["boom"], result.Errors[0].Args)
        );
    }

    [Fact]
    internal async Task RunAsync_UnrecognizedOperation_FailsLoudlyInsteadOfSilentlyDoingNothing()
    {
        this.PassValidation();
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations.DirectoryExists(DestinationDir).Returns(true);
        this.StubOperationsSucceed();

        var result = await this.CreateSut()
            .RunAsync(Request((BackupOperation)99), this.progress, CancellationToken.None);

        Assert.Multiple(
            () => Assert.False(result.IsSuccess),
            () =>
                Assert.Equal<MessageCode>(
                    [MessageCode.UnexpectedErrorFormat],
                    Codes(result.Errors)
                )
        );

        await this.chunkedBackupService.DidNotReceive()
            .CreateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            );
        await this.chunkedBackupService.DidNotReceive()
            .UpdateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            );
        await this.chunkedBackupService.DidNotReceive()
            .RestoreAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    internal async Task RunAsync_OperationCancelled_PropagatesCancellationInsteadOfMappingIt()
    {
        this.PassValidation();
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations.DirectoryExists(DestinationDir).Returns(true);
        _ = this.chunkedBackupService
            .CreateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(new OperationCanceledException());

        _ = await Assert.ThrowsAsync<OperationCanceledException>(
            () => this.CreateSut()
                .RunAsync(Request(BackupOperation.Create), this.progress, CancellationToken.None)
        );
    }

    [Fact]
    internal async Task RunVerifyAsync_OperationCancelled_PropagatesCancellationInsteadOfMappingIt()
    {
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.chunkedBackupService
            .VerifyAsync(
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(new OperationCanceledException());

        _ = await Assert.ThrowsAsync<OperationCanceledException>(
            () => this.CreateSut()
                .RunVerifyAsync(Request(BackupOperation.Verify), this.progress, CancellationToken.None)
        );
    }

    [Fact]
    internal async Task RunVerifyAsync_ExistingArchive_SkipsValidationAndNeverWritesToADestination()
    {
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.chunkedBackupService
            .VerifyAsync(
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(SuccessResult());

        var request = Request(BackupOperation.Verify, destination: string.Empty);

        var result = await this.CreateSut()
            .RunVerifyAsync(request, this.progress, CancellationToken.None);

        Assert.True(result.Value.Completion!.IsSuccess);

        await this.chunkedBackupService.Received(1)
            .VerifyAsync(SourceDir, request, this.progress, Arg.Any<CancellationToken>());
        await this.validator.DidNotReceive()
            .AnalyzeErrorsAsync(Arg.Any<BackupRequest>(), Arg.Any<CancellationToken>());
        await this.validator.DidNotReceive()
            .AnalyzeWarningsAsync(Arg.Any<BackupRequest>(), Arg.Any<CancellationToken>());
        await this.fileOperations.DidNotReceive()
            .CreateDirectoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        this.AssertNothingDeleted();
    }

    [Fact]
    internal async Task RunVerifyAsync_WhileTheBackupIsBeingModified_ReportsBackupInUse()
    {
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations.IsLockHeld(Arg.Any<string>()).Returns(true);

        var result = await this.CreateSut()
            .RunVerifyAsync(Request(BackupOperation.Verify), this.progress, CancellationToken.None);

        Assert.Equal<MessageCode>([MessageCode.BackupInUse], Codes(result.Errors));
        await this.chunkedBackupService.DidNotReceive()
            .VerifyAsync(
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    internal async Task RunVerifyAsync_WithoutPassword_ReportsPasswordRequiredBeforeAnyProbing()
    {
        var request = Request(
            BackupOperation.Verify,
            destination: string.Empty,
            password: string.Empty
        );

        var result = await this.CreateSut()
            .RunVerifyAsync(request, this.progress, CancellationToken.None);

        Assert.Multiple(
            () => Assert.False(result.IsSuccess),
            () =>
                Assert.Equal<MessageCode>(
                    [MessageCode.PasswordRequired],
                    Codes(result.Errors)
                )
        );

        _ = this.fileOperations.DidNotReceive().DirectoryExists(Arg.Any<string>());
        await this.chunkedBackupService.DidNotReceive()
            .VerifyAsync(
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    internal async Task RunVerifyAsync_WithAnUnnormalizablePath_ReportsTheInvalidPathBeforeProbing()
    {
        var request = Request(
            BackupOperation.Verify,
            source: UnnormalizablePath(),
            destination: string.Empty
        );

        var result = await this.CreateSut()
            .RunVerifyAsync(request, this.progress, CancellationToken.None);

        Assert.Multiple(
            () => Assert.False(result.IsSuccess),
            () =>
                Assert.Equal<MessageCode>(
                    [MessageCode.PathMustBeAbsoluteFormat],
                    Codes(result.Errors)
                )
        );

        _ = this.fileOperations.DidNotReceive().DirectoryExists(Arg.Any<string>());
        await this.chunkedBackupService.DidNotReceive()
            .VerifyAsync(
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData(true, MessageCode.SourceMustBeDirectory)]
    [InlineData(false, MessageCode.SourcePathNotExist)]
    internal async Task RunVerifyAsync_SourceIsAFileOrGone_FailsWithoutReadingTheArchive(
        bool sourceIsFile,
        MessageCode expectedCode
    )
    {
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(false);
        _ = this.fileOperations.FileExists(SourceDir).Returns(sourceIsFile);

        var result = await this.CreateSut()
            .RunVerifyAsync(Request(BackupOperation.Verify), this.progress, CancellationToken.None);

        Assert.Multiple(
            () => Assert.False(result.IsSuccess),
            () => Assert.Equal<MessageCode>([expectedCode], Codes(result.Errors))
        );

        await this.chunkedBackupService.DidNotReceive()
            .VerifyAsync(
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    internal async Task RunVerifyAsync_EngineThrows_ReportsUnexpectedErrorCarryingOnlyTheMessage()
    {
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.chunkedBackupService
            .VerifyAsync(
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(new InvalidOperationException("verify boom"));

        var result = await this.CreateSut()
            .RunVerifyAsync(Request(BackupOperation.Verify), this.progress, CancellationToken.None);

        Assert.Multiple(
            () => Assert.False(result.IsSuccess),
            () =>
                Assert.Equal<MessageCode>(
                    [MessageCode.UnexpectedErrorFormat],
                    Codes(result.Errors)
                ),
            () => Assert.Equal<object>(["verify boom"], result.Errors[0].Args)
        );
    }

    [Fact]
    internal async Task RunAsync_UnnormalizablePaths_FailOnTheRawPathInsteadOfThrowing()
    {
        this.PassValidation();
        _ = this.fileOperations.DirectoryExists(Arg.Any<string>()).Returns(false);
        _ = this.fileOperations.FileExists(Arg.Any<string>()).Returns(false);
        this.StubOperationsSucceed();

        var request = Request(
            BackupOperation.Create,
            source: UnnormalizablePath(),
            destination: UnnormalizablePath()
        );

        var result = await this.CreateSut().RunAsync(request, this.progress, CancellationToken.None);

        Assert.Multiple(
            () => Assert.False(result.IsSuccess),
            () =>
                Assert.Equal<MessageCode>(
                    [MessageCode.SourcePathNotExist],
                    Codes(result.Errors)
                )
        );

        _ = this.fileOperations.Received(1).DirectoryExists(request.SourcePath);
        await this.fileOperations.DidNotReceive()
            .CreateDirectoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        this.AssertNothingDeleted();
    }
}
