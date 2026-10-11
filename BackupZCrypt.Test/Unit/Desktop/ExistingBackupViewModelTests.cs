using BackupZCrypt.Application.Backup;
using BackupZCrypt.Application.Common;
using BackupZCrypt.Application.Manifest;
using BackupZCrypt.Application.Settings;
using BackupZCrypt.Desktop.Resources;
using BackupZCrypt.Desktop.Services.Interfaces;
using BackupZCrypt.Desktop.ViewModels;

using NSubstitute;

namespace BackupZCrypt.Test.Unit.Desktop;

/// <summary>
/// Unit tests for the pages that work on an existing backup: the manifest detection that decides
/// whether the page asks for a password, the start gate layered on top of it, and the request the
/// restore, update, and verify pages each build from the same inputs. The three pages are nearly
/// identical, so every page-specific assertion here exists to catch a copy-paste between them.
/// The absorption of probe failures into the missing kind lives in the backup operations and is
/// pinned by their own tests.
/// </summary>
public sealed class ExistingBackupViewModelTests : IDisposable
{
    /// <summary>
    /// Identifies the page under test in theories that run against every existing-backup page.
    /// </summary>
    internal enum ExistingBackupPage
    {
        /// <summary>
        /// The page that restores an archive.
        /// </summary>
        Restore,

        /// <summary>
        /// The page that updates an archive.
        /// </summary>
        Update,

        /// <summary>
        /// The page that verifies an archive.
        /// </summary>
        Verify,
    }

    /// <summary>
    /// The substituted backup operations every page runs and detects its backup through.
    /// </summary>
    private readonly IBackupOperations operations = Substitute.For<IBackupOperations>();

    /// <summary>
    /// The substituted store the pages read and persist the recent paths through.
    /// </summary>
    private readonly ISettingsStore settingsStore = Substitute.For<ISettingsStore>();

    /// <summary>
    /// The substituted folder picker the browse commands go through.
    /// </summary>
    private readonly IFilePickerService filePicker = Substitute.For<IFilePickerService>();

    /// <summary>
    /// The synchronization context that was installed before the test, restored afterwards.
    /// </summary>
    private readonly SynchronizationContext? previousContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExistingBackupViewModelTests"/> class, installing
    /// a synchronization context that runs posted callbacks inline, so a detection that completes
    /// after its await resumes at a deterministic point in the test instead of on an arbitrary thread
    /// pool thread.
    /// </summary>
    public ExistingBackupViewModelTests()
    {
        this.previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new InlineSynchronizationContext());
    }

    /// <summary>
    /// Restores the synchronization context so the inline one cannot leak into other test classes
    /// that share the same thread.
    /// </summary>
    public void Dispose()
    {
        SynchronizationContext.SetSynchronizationContext(this.previousContext);
    }

    [Fact]
    internal void BackupPath_WhenAnEncryptedManifestIsFound_AsksForAPasswordAndStartsOnlyOnceItIsTyped()
    {
        StubDetection(ManifestKind.Encrypted);
        var sut = CreateSut();

        sut.DestinationPath = "restored-here";
        sut.SourcePath = "backup";

        var enabledWithoutPassword = sut.StartCommand.CanExecute(null);

        sut.Password = "backup-password";

        Assert.Multiple(
            () => Assert.True(sut.IsBackupDetected),
            () => Assert.False(sut.HasDetection),
            () => Assert.False(enabledWithoutPassword),
            () => Assert.True(sut.StartCommand.CanExecute(null))
        );
    }

    [Fact]
    internal void BackupPath_WhenNoManifestIsFound_ShowsTheMissingBackupNoticeAndKeepsTheStartBlocked()
    {
        StubDetection(ManifestKind.Missing);
        var sut = CreateSut();

        sut.DestinationPath = "restored-here";
        sut.SourcePath = "not-a-backup";
        sut.Password = "backup-password";

        Assert.Multiple(
            () => Assert.False(sut.IsBackupDetected),
            () => Assert.True(sut.HasDetection),
            () => Assert.False(sut.StartCommand.CanExecute(null))
        );
    }

    [Theory]
    [InlineData(ManifestKind.Missing)]
    [InlineData(ManifestKind.PathNotFound)]
    [InlineData(ManifestKind.NotADirectory)]
    [InlineData(ManifestKind.Damaged)]
    [InlineData(ManifestKind.Unsupported)]
    internal void BackupPath_WhenNoUsableBackupIsFound_ExplainsWhy(ManifestKind kind)
    {
        StubDetection(kind);
        var sut = CreateSut();

        sut.SourcePath = "somewhere";

        var expected = kind switch
        {
            ManifestKind.PathNotFound => Strings.DetectPathNotFound,
            ManifestKind.NotADirectory => Strings.DetectNotADirectory,
            ManifestKind.Damaged => Strings.DetectDamaged,
            ManifestKind.Unsupported => Strings.DetectUnsupported,
            _ => Strings.DetectMissing,
        };

        Assert.Multiple(
            () => Assert.True(sut.HasDetection),
            () => Assert.Equal(expected, sut.DetectionMessage),
            () => Assert.False(sut.StartCommand.CanExecute(null))
        );
    }

    [Fact]
    internal void BackupPath_WhenClearedAfterADetection_ForgetsThePasswordRequirementAndTheNotice()
    {
        StubDetection(ManifestKind.Encrypted);
        var sut = CreateSut();

        sut.DestinationPath = "restored-here";
        sut.SourcePath = "backup";
        sut.Password = "backup-password";
        var enabledWithABackup = sut.StartCommand.CanExecute(null);

        sut.SourcePath = "   ";

        Assert.Multiple(
            () => Assert.True(enabledWithABackup),
            () => Assert.False(sut.IsBackupDetected),
            () => Assert.False(sut.HasDetection),
            () => Assert.False(sut.StartCommand.CanExecute(null))
        );
    }

    [Fact]
    internal void BackupPath_WhenAnEarlierProbeCompletesLate_KeepsTheDetectionOfTheCurrentPath()
    {
        TaskCompletionSource<ManifestKind> slowProbe = new();

        _ = this
            .operations.DetectManifestKindAsync(
                "slow-backup",
                Arg.Any<CancellationToken>()
            )
            .Returns(slowProbe.Task);
        _ = this
            .operations.DetectManifestKindAsync(
                "not-a-backup",
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(ManifestKind.Missing));

        var sut = CreateSut();
        sut.DestinationPath = "restored-here";
        sut.Password = "backup-password";

        sut.SourcePath = "slow-backup";
        sut.SourcePath = "not-a-backup";
        var detectedBeforeTheStaleResult = sut.IsBackupDetected;

        slowProbe.SetResult(ManifestKind.Encrypted);

        Assert.Multiple(
            () => Assert.False(detectedBeforeTheStaleResult),
            () => Assert.False(sut.IsBackupDetected),
            () => Assert.True(sut.HasDetection),
            () => Assert.False(sut.StartCommand.CanExecute(null))
        );
    }

    [Fact]
    internal void BackupPath_WhenTheDetectionFinishesAfterThePathWasSet_ReEvaluatesTheStartCommand()
    {
        TaskCompletionSource<ManifestKind> probe = new();

        _ = this
            .operations.DetectManifestKindAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(probe.Task);

        var sut = CreateSut();
        sut.DestinationPath = "restored-here";
        sut.Password = "backup-password";

        var notifications = 0;
        sut.StartCommand.CanExecuteChanged += (_, _) => notifications++;

        sut.SourcePath = "backup";
        var notificationsWhileProbing = notifications;
        var enabledWhileProbing = sut.StartCommand.CanExecute(null);

        probe.SetResult(ManifestKind.Encrypted);

        Assert.Multiple(
            () => Assert.False(enabledWhileProbing),
            () => Assert.True(notifications > notificationsWhileProbing),
            () => Assert.True(sut.StartCommand.CanExecute(null))
        );
    }

    [Fact]
    internal async Task StartCommand_OnTheRestorePage_SendsTheRestoreRequestAndItsSuccessTitle()
    {
        StubDetection(ManifestKind.Encrypted);
        List<IProgress<BackupStatus>?> progresses = [];
        var requests = StubRestoreCapturingRequests(progresses);
        StubRememberedPaths();

        RestoreBackupViewModel sut = new(this.operations, this.settingsStore, this.filePicker)
        {
            SourcePath = "backup-source",
            DestinationPath = "backup-destination",
            Password = "backup-password",
        };

        await sut.StartCommand.ExecuteAsync(null);

        Assert.Multiple(
            () => _ = Assert.Single(requests),
            () => Assert.Equal("backup-source", requests[0].SourcePath),
            () => Assert.Equal("backup-destination", requests[0].DestinationPath),
            () => Assert.Equal("backup-password", requests[0].Password),
            () => Assert.False(requests[0].ProceedOnWarnings),
            () => Assert.NotNull(Assert.Single(progresses)),
            () => Assert.True(sut.ResultIsSuccess),
            () => Assert.Equal(Strings.ResultSuccessTitle, sut.ResultTitle)
        );
    }

    [Fact]
    internal async Task StartCommand_OnTheUpdatePage_SendsTheUpdateRequestAndItsSuccessTitle()
    {
        StubDetection(ManifestKind.Encrypted);
        List<UpdateBackupRequest> requests = [];
        List<IProgress<BackupStatus>?> progresses = [];
        _ = this
            .operations.UpdateAsync(
                Arg.Do<UpdateBackupRequest>(requests.Add),
                Arg.Do<IProgress<BackupStatus>?>(progresses.Add),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(SuccessOutcome()));
        StubRememberedPaths();

        UpdateBackupViewModel sut = new(this.operations, this.settingsStore, this.filePicker)
        {
            SourcePath = "backup-source",
            DestinationPath = "backup-destination",
            Password = "backup-password",
        };

        await sut.StartCommand.ExecuteAsync(null);

        Assert.Multiple(
            () => _ = Assert.Single(requests),
            () => Assert.Equal("backup-source", requests[0].SourcePath),
            () => Assert.Equal("backup-destination", requests[0].DestinationPath),
            () => Assert.Equal("backup-password", requests[0].Password),
            () => Assert.False(requests[0].ProceedOnWarnings),
            () => Assert.NotNull(Assert.Single(progresses)),
            () => Assert.True(sut.ResultIsSuccess),
            () => Assert.Equal(Strings.ResultSuccessTitle, sut.ResultTitle)
        );
    }

    [Fact]
    internal async Task StartCommand_OnTheVerifyPage_SendsTheVerifyRequestAndItsSuccessTitle()
    {
        StubDetection(ManifestKind.Encrypted);
        List<VerifyBackupRequest> requests = [];
        List<IProgress<BackupStatus>?> progresses = [];
        _ = this
            .operations.VerifyAsync(
                Arg.Do<VerifyBackupRequest>(requests.Add),
                Arg.Do<IProgress<BackupStatus>?>(progresses.Add),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(SuccessOutcome()));
        StubRememberedPaths();

        VerifyBackupViewModel sut = new(this.operations, this.settingsStore, this.filePicker)
        {
            SourcePath = "backup-source",
            Password = "backup-password",
        };

        await sut.StartCommand.ExecuteAsync(null);

        Assert.Multiple(
            () => _ = Assert.Single(requests),
            () => Assert.Equal("backup-source", requests[0].BackupPath),
            () => Assert.Equal("backup-password", requests[0].Password),
            () => Assert.NotNull(Assert.Single(progresses)),
            () => Assert.True(sut.ResultIsSuccess),
            () => Assert.Equal(Strings.VerifySuccessTitle, sut.ResultTitle)
        );
    }

    [Theory]
    [InlineData(ExistingBackupPage.Restore, "remembered-destination", "")]
    [InlineData(ExistingBackupPage.Update, "remembered-source", "remembered-destination")]
    [InlineData(ExistingBackupPage.Verify, "remembered-destination", "")]
    internal async Task OnNavigatedToAsync_OnEachExistingBackupPage_SeedsThePathsItActuallyOperatesOn(
        ExistingBackupPage operation,
        string expectedSource,
        string expectedDestination
    )
    {
        StubDetection(ManifestKind.Encrypted);
        var sut = CreatePage(operation);

        await sut.OnNavigatedToAsync();

        Assert.Multiple(
            () => Assert.Equal(expectedSource, sut.SourcePath),
            () => Assert.Equal(expectedDestination, sut.DestinationPath)
        );
    }

    [Theory]
    [InlineData(ExistingBackupPage.Restore, "remembered-source", "backup-source")]
    [InlineData(ExistingBackupPage.Update, "backup-source", "backup-destination")]
    [InlineData(ExistingBackupPage.Verify, "remembered-source", "backup-source")]
    internal async Task StartCommand_WhenThePageSucceeds_RemembersOnlyThePathsThatPageOwns(
        ExistingBackupPage operation,
        string expectedLastSource,
        string expectedLastDestination
    )
    {
        StubDetection(ManifestKind.Encrypted);
        StubOperationsSucceed();

        List<RecentPathSettings> saved = [];
        _ = this
            .settingsStore.SaveAsync(
                Arg.Do<RecentPathSettings>(saved.Add),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());

        var sut = CreatePage(operation);
        sut.SourcePath = "backup-source";
        sut.DestinationPath = "backup-destination";
        sut.Password = "backup-password";

        await sut.StartCommand.ExecuteAsync(null);

        RecentPathSettings[] expected = [new(expectedLastSource, expectedLastDestination)];

        Assert.Equal(expected, saved);
    }

    [Theory]
    [InlineData(ExistingBackupPage.Restore, false)]
    [InlineData(ExistingBackupPage.Update, false)]
    [InlineData(ExistingBackupPage.Verify, true)]
    internal void StartCommand_WithoutADestination_IsEnabledOnlyOnThePageThatWritesNothing(
        ExistingBackupPage operation,
        bool expected
    )
    {
        StubDetection(ManifestKind.Encrypted);
        var sut = CreatePage(operation);

        sut.SourcePath = "backup-source";
        sut.Password = "backup-password";

        Assert.Equal(expected, sut.StartCommand.CanExecute(null));
    }

    [Fact]
    internal async Task PickCommands_OnTheRestorePage_PutTheBackupInTheSourceAndTheRecoveryInTheDestination()
    {
        StubDetection(ManifestKind.Encrypted);
        StubPickedFolders("picked-backup", "picked-recovery");
        StubRememberedPaths();

        RestoreBackupViewModel sut = new(this.operations, this.settingsStore, this.filePicker);

        await sut.PickBackupFolderCommand.ExecuteAsync(null);
        await sut.PickDestinationFolderCommand.ExecuteAsync(null);

        Assert.Multiple(
            () => Assert.Equal("picked-backup", sut.SourcePath),
            () => Assert.Equal("picked-recovery", sut.DestinationPath)
        );
    }

    [Fact]
    internal async Task PickCommands_OnTheUpdatePage_PutTheBackupInTheDestinationAndTheScannedFolderInTheSource()
    {
        StubDetection(ManifestKind.Encrypted);
        StubPickedFolders("picked-scan", "picked-backup");
        StubRememberedPaths();

        UpdateBackupViewModel sut = new(this.operations, this.settingsStore, this.filePicker);

        await sut.PickSourceFolderCommand.ExecuteAsync(null);
        await sut.PickBackupFolderCommand.ExecuteAsync(null);

        Assert.Multiple(
            () => Assert.Equal("picked-scan", sut.SourcePath),
            () => Assert.Equal("picked-backup", sut.DestinationPath)
        );
    }

    [Fact]
    internal async Task PickCommand_OnTheVerifyPage_FillsTheBackupPathAndKeepsItWhenTheDialogIsDismissed()
    {
        StubDetection(ManifestKind.Encrypted);
        StubPickedFolders("picked-backup", null);
        StubRememberedPaths();

        VerifyBackupViewModel sut = new(this.operations, this.settingsStore, this.filePicker);

        await sut.PickBackupFolderCommand.ExecuteAsync(null);
        var pathAfterPicking = sut.SourcePath;

        await sut.PickBackupFolderCommand.ExecuteAsync(null);

        Assert.Multiple(
            () => Assert.Equal("picked-backup", pathAfterPicking),
            () => Assert.Equal("picked-backup", sut.SourcePath),
            () => Assert.Empty(sut.DestinationPath)
        );
    }

    /// <summary>
    /// Builds the successful outcome the substituted operations report.
    /// </summary>
    /// <returns>A successful result carrying a completed successful <see cref="BackupResult"/>.</returns>
    private static Result<BackupOutcome> SuccessOutcome()
    {
        return Result<BackupOutcome>.Success(
            BackupOutcome.Completed(new BackupResult(TimeSpan.FromSeconds(1), 16, 1, 1))
        );
    }

    /// <summary>
    /// Builds the minimal concrete page used to exercise the shared detection behaviour.
    /// </summary>
    /// <returns>The system under test.</returns>
    private TestExistingBackupViewModel CreateSut()
    {
        StubRememberedPaths();

        return new TestExistingBackupViewModel(this.operations, this.settingsStore, this.filePicker);
    }

    /// <summary>
    /// Builds the real page that performs the given operation.
    /// </summary>
    /// <param name="operation">The operation identifying the page to build.</param>
    /// <returns>The page under test.</returns>
    /// <exception cref="ArgumentOutOfRangeException">No existing-backup page performs the operation.</exception>
    private ExistingBackupViewModelBase CreatePage(ExistingBackupPage operation)
    {
        StubRememberedPaths();

        return operation switch
        {
            ExistingBackupPage.Restore => new RestoreBackupViewModel(this.operations, this.settingsStore, this.filePicker),
            ExistingBackupPage.Update => new UpdateBackupViewModel(this.operations, this.settingsStore, this.filePicker),
            ExistingBackupPage.Verify => new VerifyBackupViewModel(this.operations, this.settingsStore, this.filePicker),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
    }

    /// <summary>
    /// Makes every manifest probe report the same kind.
    /// </summary>
    /// <param name="kind">The manifest kind detection reports.</param>
    private void StubDetection(ManifestKind kind)
    {
        _ = this
            .operations.DetectManifestKindAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(kind));
    }

    /// <summary>
    /// Stubs the remembered paths, since an unstubbed store hands the page a
    /// <see langword="null"/> settings object.
    /// </summary>
    private void StubRememberedPaths()
    {
        _ = this
            .settingsStore.LoadAsync<RecentPathSettings>(Arg.Any<CancellationToken>())
            .Returns(new RecentPathSettings("remembered-source", "remembered-destination"));
        _ = this
            .settingsStore.SaveAsync(
                Arg.Any<RecentPathSettings>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
    }

    /// <summary>
    /// Makes the folder picker return the given selections in order, the last one repeating.
    /// </summary>
    /// <param name="first">The folder the first dialog returns.</param>
    /// <param name="second">The folder the next dialogs return, or <see langword="null"/> to dismiss them.</param>
    private void StubPickedFolders(string? first, string? second)
    {
        _ = this.filePicker.PickFolderAsync(Arg.Any<string>())
            .Returns(Task.FromResult(first), Task.FromResult(second));
    }

    /// <summary>
    /// Makes every backup operation report a trivial success, so a test can assert on what the page
    /// did afterwards rather than on the outcome itself.
    /// </summary>
    private void StubOperationsSucceed()
    {
        _ = this
            .operations.RestoreAsync(
                Arg.Any<RestoreBackupRequest>(),
                Arg.Any<IProgress<BackupStatus>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(SuccessOutcome()));
        _ = this
            .operations.UpdateAsync(
                Arg.Any<UpdateBackupRequest>(),
                Arg.Any<IProgress<BackupStatus>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(SuccessOutcome()));
        _ = this
            .operations.VerifyAsync(
                Arg.Any<VerifyBackupRequest>(),
                Arg.Any<IProgress<BackupStatus>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(SuccessOutcome()));
    }

    /// <summary>
    /// Makes the restore operation report a trivial success and records every request and progress
    /// sink it receives.
    /// </summary>
    /// <param name="progresses">The list the captured progress sinks are appended to.</param>
    /// <returns>The list the captured requests are appended to.</returns>
    private List<RestoreBackupRequest> StubRestoreCapturingRequests(
        List<IProgress<BackupStatus>?> progresses
    )
    {
        List<RestoreBackupRequest> requests = [];

        _ = this
            .operations.RestoreAsync(
                Arg.Do<RestoreBackupRequest>(requests.Add),
                Arg.Do<IProgress<BackupStatus>?>(progresses.Add),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(SuccessOutcome()));

        return requests;
    }

    /// <summary>
    /// A synchronization context that invokes posted callbacks on the calling thread, making a
    /// detection that completes after its await observable at a deterministic point in the test.
    /// </summary>
    private sealed class InlineSynchronizationContext : SynchronizationContext
    {
        /// <inheritdoc/>
        public override void Post(SendOrPostCallback d, object? state)
        {
            d(state);
        }

        /// <inheritdoc/>
        public override void Send(SendOrPostCallback d, object? state)
        {
            d(state);
        }
    }

    /// <summary>
    /// The minimal concrete page used to exercise the abstract detection engine: the backup lives at
    /// the source path, as it does on the restore and verify pages. Its operation is never started by
    /// the tests that use it, so it reports a canned success.
    /// </summary>
    /// <param name="operations">The backup operations backing backup detection.</param>
    /// <param name="settingsStore">The store that loads and saves the recently used paths.</param>
    /// <param name="filePicker">The folder picker service.</param>
    private sealed class TestExistingBackupViewModel(
        IBackupOperations operations,
        ISettingsStore settingsStore,
        IFilePickerService filePicker
    ) : ExistingBackupViewModelBase(operations, settingsStore, filePicker)
    {
        /// <inheritdoc/>
        protected override string BackupPath => SourcePath;

        /// <inheritdoc/>
        protected override Task<Result<BackupOutcome>> ExecuteOperationAsync(
            bool proceedOnWarnings,
            IProgress<BackupStatus> progress,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(SuccessOutcome());
        }
    }
}
