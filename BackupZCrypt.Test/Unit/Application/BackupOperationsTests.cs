using BackupZCrypt.Application.Backup;
using BackupZCrypt.Application.Common;
using BackupZCrypt.Application.Engine;
using BackupZCrypt.Application.FileSystem;
using BackupZCrypt.Application.Manifest;
using BackupZCrypt.Domain.Archive;
using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Test.Common;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BackupZCrypt.Test.Unit.Application;

/// <summary>
/// Unit tests for the backup operations: the validation gate, the preview of an update or restore,
/// the post-validation source and destination checks, the backup lock, operation dispatch,
/// cancellation, unexpected-error mapping, and the classification of a picked backup folder. The
/// validator, file system, storage, backup service, and manifest service are all substituted, so
/// nothing here touches the real disk.
/// </summary>
/// <remarks>
/// The operations must never delete user data. A create used to empty an existing destination before it
/// started; the assertions that no deletion was received guard against that coming back.
/// </remarks>
public sealed class BackupOperationsTests
{
    /// <summary>
    /// The password every request carries, also used as the confirmation so the two always match.
    /// </summary>
    private const string Password = "Correct-Horse-Battery-Staple-42";

    /// <summary>
    /// Identifies the validated operation a request asks for, for theories that run against each.
    /// </summary>
    internal enum RunOperation
    {
        /// <summary>
        /// Creates a new archive.
        /// </summary>
        Create,

        /// <summary>
        /// Updates an existing archive.
        /// </summary>
        Update,

        /// <summary>
        /// Restores an existing archive.
        /// </summary>
        Restore,
    }

    /// <summary>
    /// The rooted source path the requests point at. Nothing is created on disk because the file
    /// system is substituted, but the path must be absolute to survive path normalization.
    /// </summary>
    private static readonly string SourceDir = Path.GetFullPath(
        Path.Join(Path.GetTempPath(), "bzc-operations-src")
    );

    /// <summary>
    /// The rooted destination path the requests point at, kept distinct from <see cref="SourceDir"/>.
    /// </summary>
    private static readonly string DestinationDir = Path.GetFullPath(
        Path.Join(Path.GetTempPath(), "bzc-operations-dst")
    );

    /// <summary>
    /// The substituted validator, which lets a test choose exactly which blocking errors and advisory
    /// warnings the operations see without depending on the real validator's file-system probes.
    /// </summary>
    private readonly IBackupRequestValidator validator =
        Substitute.For<IBackupRequestValidator>();

    /// <summary>
    /// The substituted file system the operations probe for the source and destination.
    /// </summary>
    private readonly IFileOperationsService fileOperations =
        Substitute.For<IFileOperationsService>();

    /// <summary>
    /// The substituted lock service the operations take and probe the backup's lock through.
    /// </summary>
    private readonly IFileLockService fileLock = Substitute.For<IFileLockService>();

    /// <summary>
    /// The substituted backup engine the operations dispatch each run to.
    /// </summary>
    private readonly IChunkedBackupService chunkedBackupService =
        Substitute.For<IChunkedBackupService>();

    /// <summary>
    /// The substituted storage service the preview warnings query for free space.
    /// </summary>
    private readonly ISystemStorageService systemStorage = Substitute.For<ISystemStorageService>();

    /// <summary>
    /// The substituted manifest service a picked backup folder is classified through.
    /// </summary>
    private readonly IManifestService manifestService = Substitute.For<IManifestService>();

    /// <summary>
    /// The progress sink handed to the operations; assertions check it is forwarded unchanged.
    /// </summary>
    private readonly RecordingProgress<BackupStatus> progress = new();

    /// <summary>
    /// Creates the operations wired to the substituted dependencies.
    /// </summary>
    /// <returns>The system under test.</returns>
    private BackupOperations CreateSut()
    {
        return new(
            this.validator,
            this.fileOperations,
            this.fileLock,
            this.chunkedBackupService,
            this.systemStorage,
            this.manifestService
        );
    }

    /// <summary>
    /// Runs a create, update, or restore request through the operation matching its type, so a
    /// theory can drive the shared pipeline with each of them.
    /// </summary>
    /// <param name="request">The request to run.</param>
    /// <param name="progress">The sink that receives incremental status updates, or <see langword="null"/> to discard them.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The outcome of the operation.</returns>
    private Task<Result<BackupOutcome>> RunAsync(
        BackupRequest request,
        IProgress<BackupStatus>? progress,
        CancellationToken cancellationToken
    )
    {
        var sut = this.CreateSut();

        return request switch
        {
            CreateBackupRequest create => sut.CreateAsync(create, progress, cancellationToken),
            UpdateBackupRequest update => sut.UpdateAsync(update, progress, cancellationToken),
            RestoreBackupRequest restore => sut.RestoreAsync(restore, progress, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };
    }

    /// <summary>
    /// Builds a request whose fields are individually valid so a test only varies what it exercises.
    /// </summary>
    /// <param name="operation">The operation the request asks for.</param>
    /// <param name="proceedOnWarnings">Whether the user agreed to proceed past advisory warnings.</param>
    /// <param name="source">The source path; defaults to <see cref="SourceDir"/>.</param>
    /// <param name="destination">The destination path; defaults to <see cref="DestinationDir"/>.</param>
    /// <param name="password">The password, also used as the confirmation so the two always match.</param>
    /// <returns>A request of the operation's type built from the supplied values.</returns>
    private static BackupRequest Request(
        RunOperation operation,
        bool proceedOnWarnings = false,
        string? source = null,
        string? destination = null,
        string password = Password
    )
    {
        var sourcePath = source ?? SourceDir;
        var destinationPath = destination ?? DestinationDir;

        return operation switch
        {
            RunOperation.Create => new CreateBackupRequest(
                sourcePath,
                destinationPath,
                password,
                password,
                EncryptionAlgorithm.Aes,
                KeyDerivationAlgorithm.PBKDF2,
                CompressionMode.None,
                proceedOnWarnings
            ),
            RunOperation.Update => new UpdateBackupRequest(
                sourcePath,
                destinationPath,
                password,
                proceedOnWarnings
            ),
            _ => new RestoreBackupRequest(sourcePath, destinationPath, password, proceedOnWarnings),
        };
    }

    /// <summary>
    /// Determines whether the algorithms the engine received are exactly the ones a create request
    /// chose.
    /// </summary>
    /// <param name="algorithms">The algorithms the engine received.</param>
    /// <param name="request">The request the operation was started with.</param>
    /// <returns><see langword="true"/> when the request is a create naming those algorithms.</returns>
    private static bool MatchesAlgorithmsOf(ManifestHeader algorithms, BackupRequest request)
    {
        return request is CreateBackupRequest create
            && algorithms
                == new ManifestHeader(
                    create.EncryptionAlgorithm,
                    create.KeyDerivationAlgorithm,
                    create.Compression
                );
    }

    /// <summary>
    /// Builds a verify request for the archive at <see cref="SourceDir"/> unless told otherwise.
    /// </summary>
    /// <param name="backup">The archive path; defaults to <see cref="SourceDir"/>.</param>
    /// <param name="password">The password the archive is opened with.</param>
    /// <returns>The verify request.</returns>
    private static VerifyBackupRequest VerifyRequest(string? backup = null, string password = Password)
    {
        return new(backup ?? SourceDir, password);
    }

    /// <summary>
    /// Appends a redundant <c>.</c> segment so the raw path differs from its normalized form, letting
    /// a test prove the operations hand the resolved path to the backup service.
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
                Arg.Any<string>(),
                Arg.Any<ManifestHeader>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(SuccessResult());
        _ = this.chunkedBackupService
            .UpdateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(SuccessResult());
        _ = this.chunkedBackupService
            .RestoreAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(SuccessResult());
    }

    /// <summary>
    /// Asserts that the operations never asked the file system to delete anything.
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

        var result = await this.RunAsync(Request(RunOperation.Create), this.progress, CancellationToken.None);

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
                Arg.Any<string>(),
                Arg.Any<ManifestHeader>(),
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

        var request = Request(RunOperation.Create, proceedOnWarnings: proceedOnWarnings);

        var result = await this.RunAsync(request, this.progress, CancellationToken.None);

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
            .CreateAsync(
                SourceDir,
                DestinationDir,
                request.Password,
                Arg.Is<ManifestHeader>(h => MatchesAlgorithmsOf(h, request)),
                this.progress,
                Arg.Any<CancellationToken>()
            );
        this.AssertNothingDeleted();
    }

    [Theory]
    [InlineData(RunOperation.Create, true)]
    [InlineData(RunOperation.Create, false)]
    [InlineData(RunOperation.Update, true)]
    [InlineData(RunOperation.Restore, true)]
    [InlineData(RunOperation.Restore, false)]
    internal async Task RunAsync_SuccessfulOperation_NeverDeletesAnythingAndOnlyACreatePreparesTheDestination(
        RunOperation operation,
        bool destinationExists
    )
    {
        this.PassValidation();
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations.DirectoryExists(DestinationDir).Returns(destinationExists);
        this.StubOperationsSucceed();

        var result = await this.RunAsync(Request(operation), this.progress, CancellationToken.None);

        Assert.True(result.Value.Completion!.IsSuccess);

        await this.fileOperations.Received(operation is RunOperation.Create ? 1 : 0)
            .CreateDirectoryAsync(DestinationDir, Arg.Any<CancellationToken>());
        this.AssertNothingDeleted();
    }

    [Theory]
    [InlineData(RunOperation.Create)]
    [InlineData(RunOperation.Update)]
    internal async Task RunAsync_WritingOperation_HoldsTheBackupLockForTheWholeRun(RunOperation operation)
    {
        this.PassValidation();
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations.DirectoryExists(DestinationDir).Returns(true);
        var lockFile = Path.Join(DestinationDir, BackupConstants.LockFileName);
        var backupLock = Substitute.For<IDisposable>();
        _ = this.fileLock.AcquireExclusiveLock(lockFile).Returns(backupLock);
        this.StubOperationsSucceed();

        _ = await this.RunAsync(Request(operation), this.progress, CancellationToken.None);

        Received.InOrder(() =>
        {
            _ = this.fileLock.AcquireExclusiveLock(lockFile);
            backupLock.Dispose();
        });
    }

    [Theory]
    [InlineData(RunOperation.Create)]
    [InlineData(RunOperation.Update)]
    internal async Task RunAsync_BackupLockedByAnotherOperation_ReportsBackupInUseWithoutRunning(
        RunOperation operation
    )
    {
        this.PassValidation();
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations.DirectoryExists(DestinationDir).Returns(true);
        _ = this.fileLock
            .AcquireExclusiveLock(Arg.Any<string>())
            .Throws(new IOException("in use"));
        this.StubOperationsSucceed();

        var result = await this.RunAsync(Request(operation), this.progress, CancellationToken.None);

        Assert.Equal<MessageCode>([MessageCode.BackupInUse], Codes(result.Errors));
        await this.chunkedBackupService.DidNotReceive()
            .CreateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<ManifestHeader>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            );
        await this.chunkedBackupService.DidNotReceive()
            .UpdateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    internal async Task RunAsync_RestoreWhileTheBackupIsBeingModified_ReportsBackupInUse()
    {
        this.PassValidation();
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileLock.IsLockHeld(Arg.Any<string>()).Returns(true);
        this.StubOperationsSucceed();

        var result = await this.RunAsync(Request(RunOperation.Restore), this.progress, CancellationToken.None);

        Assert.Equal<MessageCode>([MessageCode.BackupInUse], Codes(result.Errors));
        await this.chunkedBackupService.DidNotReceive()
            .RestoreAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
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
                Arg.Any<string>(),
                Arg.Any<ManifestHeader>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result<BackupResult>.Failure(MessageCode.AllFilesFailed));

        var result = await this.RunAsync(Request(RunOperation.Create), this.progress, CancellationToken.None);

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
                Arg.Any<string>(),
                Arg.Any<ManifestHeader>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result<BackupResult>.Failure(MessageCode.AllFilesFailed));

        _ = await this.RunAsync(Request(RunOperation.Create), this.progress, CancellationToken.None);

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

        var result = await this.RunAsync(Request(RunOperation.Update), this.progress, CancellationToken.None);

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
                Arg.Any<string>(),
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

        var result = await this.RunAsync(Request(RunOperation.Update), this.progress, CancellationToken.None);

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

        var result = await this.RunAsync(
            Request(RunOperation.Update, proceedOnWarnings: true),
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

        var result = await this.RunAsync(Request(RunOperation.Restore), this.progress, CancellationToken.None);

        Assert.Equal<MessageCode>([MessageCode.InvalidPassword], Codes(result.Errors));
        await this.fileOperations.DidNotReceive()
            .CreateDirectoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await this.chunkedBackupService.DidNotReceive()
            .RestoreAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
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

        var result = await this.RunAsync(Request(RunOperation.Restore), this.progress, CancellationToken.None);

        Assert.Equal<MessageCode>([MessageCode.LowDiskSpaceFormat], Codes(result.Value.PendingWarnings));
    }

    [Theory]
    [InlineData(RunOperation.Create)]
    [InlineData(RunOperation.Update)]
    [InlineData(RunOperation.Restore)]
    internal async Task RunAsync_KnownOperation_ForwardsNormalizedPathsAndTokenToItsOwnMethod(
        RunOperation operation
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

        var result = await this.RunAsync(request, this.progress, cancellation.Token);

        Assert.True(result.Value.Completion!.IsSuccess);

        switch (operation)
        {
            case RunOperation.Create:
                await this.chunkedBackupService.Received(1)
                    .CreateAsync(
                        SourceDir,
                        DestinationDir,
                        request.Password,
                        Arg.Is<ManifestHeader>(h => MatchesAlgorithmsOf(h, request)),
                        this.progress,
                        cancellation.Token
                    );
                break;
            case RunOperation.Update:
                await this.chunkedBackupService.Received(1)
                    .UpdateAsync(
                        SourceDir,
                        DestinationDir,
                        request.Password,
                        this.progress,
                        cancellation.Token
                    );
                break;
            default:
                await this.chunkedBackupService.Received(1)
                    .RestoreAsync(
                        SourceDir,
                        DestinationDir,
                        request.Password,
                        this.progress,
                        cancellation.Token
                    );
                break;
        }

        _ = Assert.Single(
            this.chunkedBackupService.ReceivedCalls(),
            static call =>
                call.GetMethodInfo().Name
                    is nameof(IChunkedBackupService.CreateAsync)
                        or nameof(IChunkedBackupService.UpdateAsync)
                        or nameof(IChunkedBackupService.RestoreAsync)
                        or nameof(IChunkedBackupService.VerifyAsync)
        );
    }

    [Fact]
    internal async Task CreateAsync_ForwardsThePasswordAndEveryChosenAlgorithmToTheEngine()
    {
        this.PassValidation();
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations.DirectoryExists(DestinationDir).Returns(true);
        this.StubOperationsSucceed();

        var request = new CreateBackupRequest(
            SourceDir,
            DestinationDir,
            Password,
            "A-Different-Confirmation-7",
            EncryptionAlgorithm.Twofish,
            KeyDerivationAlgorithm.Scrypt,
            CompressionMode.ZstdBest
        );

        _ = await this.CreateSut().CreateAsync(request, this.progress, CancellationToken.None);

        await this.chunkedBackupService.Received(1)
            .CreateAsync(
                SourceDir,
                DestinationDir,
                Password,
                new ManifestHeader(
                    EncryptionAlgorithm.Twofish,
                    KeyDerivationAlgorithm.Scrypt,
                    CompressionMode.ZstdBest
                ),
                this.progress,
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

        _ = await this.RunAsync(Request(RunOperation.Create), null, CancellationToken.None);

        await this.chunkedBackupService.Received(1)
            .CreateAsync(
                SourceDir,
                DestinationDir,
                Arg.Any<string>(),
                Arg.Any<ManifestHeader>(),
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

        var result = await this.RunAsync(Request(RunOperation.Create), this.progress, CancellationToken.None);

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

        var result = await this.RunAsync(
            Request(RunOperation.Update, proceedOnWarnings: true),
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
                Arg.Any<string>(),
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
                Arg.Any<string>(),
                Arg.Any<ManifestHeader>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(new InvalidOperationException("boom"));

        var result = await this.RunAsync(Request(RunOperation.Create), this.progress, CancellationToken.None);

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
    internal async Task RunAsync_OperationCancelled_PropagatesCancellationInsteadOfMappingIt()
    {
        this.PassValidation();
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations.DirectoryExists(DestinationDir).Returns(true);
        _ = this.chunkedBackupService
            .CreateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<ManifestHeader>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(new OperationCanceledException());

        _ = await Assert.ThrowsAsync<OperationCanceledException>(
            () => this.RunAsync(Request(RunOperation.Create), this.progress, CancellationToken.None)
        );
    }

    [Fact]
    internal async Task VerifyAsync_OperationCancelled_PropagatesCancellationInsteadOfMappingIt()
    {
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.chunkedBackupService
            .VerifyAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(new OperationCanceledException());

        _ = await Assert.ThrowsAsync<OperationCanceledException>(
            () => this.CreateSut()
                .VerifyAsync(VerifyRequest(), this.progress, CancellationToken.None)
        );
    }

    [Fact]
    internal async Task VerifyAsync_ExistingArchive_SkipsValidationAndNeverWritesToADestination()
    {
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.chunkedBackupService
            .VerifyAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(SuccessResult());

        var request = VerifyRequest();

        var result = await this.CreateSut()
            .VerifyAsync(request, this.progress, CancellationToken.None);

        Assert.True(result.Value.Completion!.IsSuccess);

        await this.chunkedBackupService.Received(1)
            .VerifyAsync(SourceDir, request.Password, this.progress, Arg.Any<CancellationToken>());
        await this.validator.DidNotReceive()
            .AnalyzeErrorsAsync(Arg.Any<BackupRequest>(), Arg.Any<CancellationToken>());
        await this.validator.DidNotReceive()
            .AnalyzeWarningsAsync(Arg.Any<BackupRequest>(), Arg.Any<CancellationToken>());
        await this.fileOperations.DidNotReceive()
            .CreateDirectoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        this.AssertNothingDeleted();
    }

    [Fact]
    internal async Task VerifyAsync_WhileTheBackupIsBeingModified_ReportsBackupInUse()
    {
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileLock.IsLockHeld(Arg.Any<string>()).Returns(true);

        var result = await this.CreateSut()
            .VerifyAsync(VerifyRequest(), this.progress, CancellationToken.None);

        Assert.Equal<MessageCode>([MessageCode.BackupInUse], Codes(result.Errors));
        await this.chunkedBackupService.DidNotReceive()
            .VerifyAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    internal async Task VerifyAsync_WithoutPassword_ReportsPasswordRequiredBeforeAnyProbing()
    {
        var request = VerifyRequest(password: string.Empty);

        var result = await this.CreateSut()
            .VerifyAsync(request, this.progress, CancellationToken.None);

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
                Arg.Any<string>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    internal async Task VerifyAsync_WithAnUnnormalizablePath_ReportsTheInvalidPathBeforeProbing()
    {
        var request = VerifyRequest(backup: UnnormalizablePath());

        var result = await this.CreateSut()
            .VerifyAsync(request, this.progress, CancellationToken.None);

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
                Arg.Any<string>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData(true, MessageCode.SourceMustBeDirectory)]
    [InlineData(false, MessageCode.SourcePathNotExist)]
    internal async Task VerifyAsync_SourceIsAFileOrGone_FailsWithoutReadingTheArchive(
        bool sourceIsFile,
        MessageCode expectedCode
    )
    {
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(false);
        _ = this.fileOperations.FileExists(SourceDir).Returns(sourceIsFile);

        var result = await this.CreateSut()
            .VerifyAsync(VerifyRequest(), this.progress, CancellationToken.None);

        Assert.Multiple(
            () => Assert.False(result.IsSuccess),
            () => Assert.Equal<MessageCode>([expectedCode], Codes(result.Errors))
        );

        await this.chunkedBackupService.DidNotReceive()
            .VerifyAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    internal async Task VerifyAsync_EngineThrows_ReportsUnexpectedErrorCarryingOnlyTheMessage()
    {
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.chunkedBackupService
            .VerifyAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(new InvalidOperationException("verify boom"));

        var result = await this.CreateSut()
            .VerifyAsync(VerifyRequest(), this.progress, CancellationToken.None);

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
            RunOperation.Create,
            source: UnnormalizablePath(),
            destination: UnnormalizablePath()
        );

        var result = await this.RunAsync(request, this.progress, CancellationToken.None);

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

    [Theory]
    [InlineData(ManifestKind.Missing)]
    [InlineData(ManifestKind.Encrypted)]
    [InlineData(ManifestKind.Damaged)]
    internal async Task DetectManifestKindAsync_ProbeSucceeds_ReturnsTheDetectedKind(ManifestKind kind)
    {
        _ = this.manifestService
            .DetectManifestKindAsync(SourceDir, Arg.Any<CancellationToken>())
            .Returns(kind);

        var result = await this.CreateSut()
            .DetectManifestKindAsync(SourceDir, TestContext.Current.CancellationToken);

        Assert.Equal(kind, result);
    }

    [Fact]
    internal async Task DetectManifestKindAsync_PathPastedWithQuotes_IsProbedWithoutThem()
    {
        _ = this.manifestService
            .DetectManifestKindAsync(SourceDir, Arg.Any<CancellationToken>())
            .Returns(ManifestKind.Encrypted);

        var result = await this.CreateSut()
            .DetectManifestKindAsync($"\"{SourceDir}\"", TestContext.Current.CancellationToken);

        Assert.Equal(ManifestKind.Encrypted, result);
    }

    [Theory]
    [InlineData("some-backup")]
    [InlineData("")]
    [InlineData("   ")]
    internal async Task DetectManifestKindAsync_RelativeOrBlankPath_IsReportedAsNotFoundWithoutProbing(
        string path
    )
    {
        var result = await this.CreateSut()
            .DetectManifestKindAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(ManifestKind.PathNotFound, result);
        _ = await this.manifestService.DidNotReceive()
            .DetectManifestKindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    internal async Task DetectManifestKindAsync_ProbeThrows_ReportsMissingInsteadOfLeakingTheException()
    {
        _ = this.manifestService
            .DetectManifestKindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new UnauthorizedAccessException("locked"));

        var result = await this.CreateSut()
            .DetectManifestKindAsync(SourceDir, TestContext.Current.CancellationToken);

        Assert.Equal(ManifestKind.Missing, result);
    }

    [Fact]
    internal async Task DetectManifestKindAsync_ProbeCancelled_PropagatesCancellationInsteadOfMappingIt()
    {
        _ = this.manifestService
            .DetectManifestKindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        _ = await Assert.ThrowsAsync<OperationCanceledException>(
            () => this.CreateSut()
                .DetectManifestKindAsync(SourceDir, TestContext.Current.CancellationToken)
        );
    }
}
