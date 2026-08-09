using BackupZCrypt.Application.Commands;
using BackupZCrypt.Application.Commands.Interfaces;
using BackupZCrypt.Application.Queries;
using BackupZCrypt.Application.Queries.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Application.ValueObjects.Backup;
using BackupZCrypt.Application.ValueObjects.Manifest;
using BackupZCrypt.Application.ValueObjects.Settings;
using BackupZCrypt.Desktop.Resources;
using BackupZCrypt.Desktop.Services.Interfaces;
using BackupZCrypt.Desktop.ViewModels;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.ValueObjects.Backup;

using NSubstitute;

namespace BackupZCrypt.Test.Unit.Desktop;

/// <summary>
/// Unit tests for the pages that work on an existing backup: the manifest detection that decides
/// whether the page asks for a password, the start gate layered on top of it, and the message the
/// restore, update, and verify pages each build from the same inputs. The three pages are nearly
/// identical, so every page-specific assertion here exists to catch a copy-paste between them.
/// The absorption of probe failures into the missing kind lives in the detection handler and is
/// pinned by its own tests.
/// </summary>
public sealed class ExistingBackupViewModelTests : IDisposable
{
    /// <summary>
    /// The substituted handler the restore page dispatches its runs to.
    /// </summary>
    private readonly ICommandHandler<RestoreBackupCommand, Result<BackupOutcome>> restoreBackup =
        Substitute.For<ICommandHandler<RestoreBackupCommand, Result<BackupOutcome>>>();

    /// <summary>
    /// The substituted handler the update page dispatches its runs to.
    /// </summary>
    private readonly ICommandHandler<UpdateBackupCommand, Result<BackupOutcome>> updateBackup =
        Substitute.For<ICommandHandler<UpdateBackupCommand, Result<BackupOutcome>>>();

    /// <summary>
    /// The substituted handler the verify page dispatches its runs to.
    /// </summary>
    private readonly IQueryHandler<VerifyBackupQuery, Result<BackupOutcome>> verifyBackup =
        Substitute.For<IQueryHandler<VerifyBackupQuery, Result<BackupOutcome>>>();

    /// <summary>
    /// The substituted handler the pages read the recent paths through.
    /// </summary>
    private readonly IQueryHandler<GetSettingsQuery<RecentPathSettings>, RecentPathSettings> recentPathsQuery =
        Substitute.For<IQueryHandler<GetSettingsQuery<RecentPathSettings>, RecentPathSettings>>();

    /// <summary>
    /// The substituted handler the pages persist the recent paths through.
    /// </summary>
    private readonly ICommandHandler<SaveSettingsCommand<RecentPathSettings>, Result> saveRecentPaths =
        Substitute.For<ICommandHandler<SaveSettingsCommand<RecentPathSettings>, Result>>();

    /// <summary>
    /// The substituted folder picker the browse commands go through.
    /// </summary>
    private readonly IFilePickerService filePicker = Substitute.For<IFilePickerService>();

    /// <summary>
    /// The substituted handler backing backup detection.
    /// </summary>
    private readonly IQueryHandler<DetectManifestKindQuery, ManifestKind> detectManifestKind =
        Substitute.For<IQueryHandler<DetectManifestKindQuery, ManifestKind>>();

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
            .detectManifestKind.HandleAsync(
                new DetectManifestKindQuery("slow-backup"),
                Arg.Any<CancellationToken>()
            )
            .Returns(slowProbe.Task);
        _ = this
            .detectManifestKind.HandleAsync(
                new DetectManifestKindQuery("not-a-backup"),
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
            .detectManifestKind.HandleAsync(
                Arg.Any<DetectManifestKindQuery>(),
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
    internal async Task StartCommand_OnTheRestorePage_SendsTheRestoreMessageAndItsSuccessTitle()
    {
        StubDetection(ManifestKind.Encrypted);
        var commands = StubRestoreCapturingCommands();
        StubRememberedPaths();

        RestoreBackupViewModel sut = new(
            this.restoreBackup,
            this.recentPathsQuery,
            this.saveRecentPaths,
            this.filePicker,
            this.detectManifestKind
        )
        {
            SourcePath = "backup-source",
            DestinationPath = "backup-destination",
            Password = "backup-password",
        };

        await sut.StartCommand.ExecuteAsync(null);

        Assert.Multiple(
            () => _ = Assert.Single(commands),
            () => Assert.Equal("backup-source", commands[0].BackupPath),
            () => Assert.Equal("backup-destination", commands[0].DestinationPath),
            () => Assert.Equal("backup-password", commands[0].Password),
            () => Assert.False(commands[0].ProceedOnWarnings),
            () => Assert.NotNull(commands[0].Progress),
            () => Assert.True(sut.ResultIsSuccess),
            () => Assert.Equal(Strings.ResultSuccessTitle, sut.ResultTitle)
        );
    }

    [Fact]
    internal async Task StartCommand_OnTheUpdatePage_SendsTheUpdateMessageAndItsSuccessTitle()
    {
        StubDetection(ManifestKind.Encrypted);
        List<UpdateBackupCommand> commands = [];
        _ = this
            .updateBackup.HandleAsync(
                Arg.Do<UpdateBackupCommand>(commands.Add),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(SuccessOutcome()));
        StubRememberedPaths();

        UpdateBackupViewModel sut = new(
            this.updateBackup,
            this.recentPathsQuery,
            this.saveRecentPaths,
            this.filePicker,
            this.detectManifestKind
        )
        {
            SourcePath = "backup-source",
            DestinationPath = "backup-destination",
            Password = "backup-password",
        };

        await sut.StartCommand.ExecuteAsync(null);

        Assert.Multiple(
            () => _ = Assert.Single(commands),
            () => Assert.Equal("backup-source", commands[0].SourcePath),
            () => Assert.Equal("backup-destination", commands[0].BackupPath),
            () => Assert.Equal("backup-password", commands[0].Password),
            () => Assert.False(commands[0].ProceedOnWarnings),
            () => Assert.NotNull(commands[0].Progress),
            () => Assert.True(sut.ResultIsSuccess),
            () => Assert.Equal(Strings.ResultSuccessTitle, sut.ResultTitle)
        );
    }

    [Fact]
    internal async Task StartCommand_OnTheVerifyPage_SendsTheVerifyMessageAndItsSuccessTitle()
    {
        StubDetection(ManifestKind.Encrypted);
        List<VerifyBackupQuery> queries = [];
        _ = this
            .verifyBackup.HandleAsync(
                Arg.Do<VerifyBackupQuery>(queries.Add),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(SuccessOutcome()));
        StubRememberedPaths();

        VerifyBackupViewModel sut = new(
            this.verifyBackup,
            this.recentPathsQuery,
            this.saveRecentPaths,
            this.filePicker,
            this.detectManifestKind
        )
        {
            SourcePath = "backup-source",
            Password = "backup-password",
        };

        await sut.StartCommand.ExecuteAsync(null);

        Assert.Multiple(
            () => _ = Assert.Single(queries),
            () => Assert.Equal("backup-source", queries[0].BackupPath),
            () => Assert.Equal("backup-password", queries[0].Password),
            () => Assert.NotNull(queries[0].Progress),
            () => Assert.True(sut.ResultIsSuccess),
            () => Assert.Equal(Strings.VerifySuccessTitle, sut.ResultTitle)
        );
    }

    [Theory]
    [InlineData(BackupOperation.Restore, "remembered-destination", "")]
    [InlineData(BackupOperation.Update, "remembered-source", "remembered-destination")]
    [InlineData(BackupOperation.Verify, "remembered-destination", "")]
    internal async Task OnNavigatedToAsync_OnEachExistingBackupPage_SeedsThePathsItActuallyOperatesOn(
        BackupOperation operation,
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
    [InlineData(BackupOperation.Restore, "backup-source", "backup-destination")]
    [InlineData(BackupOperation.Update, "backup-source", "backup-destination")]
    [InlineData(BackupOperation.Verify, "remembered-source", "backup-source")]
    internal async Task StartCommand_WhenThePageSucceeds_RemembersOnlyThePathsThatPageOwns(
        BackupOperation operation,
        string expectedLastSource,
        string expectedLastDestination
    )
    {
        StubDetection(ManifestKind.Encrypted);
        StubOperationsSucceed();

        List<RecentPathSettings> saved = [];
        _ = this
            .saveRecentPaths.HandleAsync(
                Arg.Do<SaveSettingsCommand<RecentPathSettings>>(command => saved.Add(command.Settings)),
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
    [InlineData(BackupOperation.Restore, false)]
    [InlineData(BackupOperation.Update, false)]
    [InlineData(BackupOperation.Verify, true)]
    internal void StartCommand_WithoutADestination_IsEnabledOnlyOnThePageThatWritesNothing(
        BackupOperation operation,
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

        RestoreBackupViewModel sut = new(
            this.restoreBackup,
            this.recentPathsQuery,
            this.saveRecentPaths,
            this.filePicker,
            this.detectManifestKind
        );

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

        UpdateBackupViewModel sut = new(
            this.updateBackup,
            this.recentPathsQuery,
            this.saveRecentPaths,
            this.filePicker,
            this.detectManifestKind
        );

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

        VerifyBackupViewModel sut = new(
            this.verifyBackup,
            this.recentPathsQuery,
            this.saveRecentPaths,
            this.filePicker,
            this.detectManifestKind
        );

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
    /// Builds the successful outcome the substituted handlers report.
    /// </summary>
    /// <returns>A successful result carrying a completed successful <see cref="BackupResult"/>.</returns>
    private static Result<BackupOutcome> SuccessOutcome()
    {
        return Result<BackupOutcome>.Success(
            BackupOutcome.Completed(new BackupResult(true, TimeSpan.FromSeconds(1), 16, 1, 1))
        );
    }

    /// <summary>
    /// Builds the minimal concrete page used to exercise the shared detection behaviour.
    /// </summary>
    /// <returns>The system under test.</returns>
    private TestExistingBackupViewModel CreateSut()
    {
        StubRememberedPaths();

        return new TestExistingBackupViewModel(
            this.recentPathsQuery,
            this.saveRecentPaths,
            this.filePicker,
            this.detectManifestKind
        );
    }

    /// <summary>
    /// Builds the real page that performs the given operation.
    /// </summary>
    /// <param name="operation">The operation identifying the page to build.</param>
    /// <returns>The page under test.</returns>
    /// <exception cref="ArgumentOutOfRangeException">No existing-backup page performs the operation.</exception>
    private ExistingBackupViewModelBase CreatePage(BackupOperation operation)
    {
        StubRememberedPaths();

        return operation switch
        {
            BackupOperation.Restore => new RestoreBackupViewModel(
                this.restoreBackup,
                this.recentPathsQuery,
                this.saveRecentPaths,
                this.filePicker,
                this.detectManifestKind
            ),
            BackupOperation.Update => new UpdateBackupViewModel(
                this.updateBackup,
                this.recentPathsQuery,
                this.saveRecentPaths,
                this.filePicker,
                this.detectManifestKind
            ),
            BackupOperation.Verify => new VerifyBackupViewModel(
                this.verifyBackup,
                this.recentPathsQuery,
                this.saveRecentPaths,
                this.filePicker,
                this.detectManifestKind
            ),
            BackupOperation.Create => throw new ArgumentOutOfRangeException(nameof(operation)),
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
            .detectManifestKind.HandleAsync(
                Arg.Any<DetectManifestKindQuery>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(kind));
    }

    /// <summary>
    /// Stubs the remembered paths, since an unstubbed handler hands the page a
    /// <see langword="null"/> settings object.
    /// </summary>
    private void StubRememberedPaths()
    {
        _ = this
            .recentPathsQuery.HandleAsync(
                Arg.Any<GetSettingsQuery<RecentPathSettings>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new RecentPathSettings("remembered-source", "remembered-destination"));
        _ = this
            .saveRecentPaths.HandleAsync(
                Arg.Any<SaveSettingsCommand<RecentPathSettings>>(),
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
    /// Makes every operation handler report a trivial success, so a test can assert on what the page
    /// did afterwards rather than on the outcome itself.
    /// </summary>
    private void StubOperationsSucceed()
    {
        _ = this
            .restoreBackup.HandleAsync(Arg.Any<RestoreBackupCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessOutcome()));
        _ = this
            .updateBackup.HandleAsync(Arg.Any<UpdateBackupCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessOutcome()));
        _ = this
            .verifyBackup.HandleAsync(Arg.Any<VerifyBackupQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessOutcome()));
    }

    /// <summary>
    /// Makes the restore handler report a trivial success and records every command it receives.
    /// </summary>
    /// <returns>The list the captured commands are appended to.</returns>
    private List<RestoreBackupCommand> StubRestoreCapturingCommands()
    {
        List<RestoreBackupCommand> commands = [];

        _ = this
            .restoreBackup.HandleAsync(
                Arg.Do<RestoreBackupCommand>(commands.Add),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(SuccessOutcome()));

        return commands;
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
    /// <param name="recentPathsQuery">The handler that loads the recently used paths.</param>
    /// <param name="saveRecentPathsCommand">The handler that persists the recently used paths.</param>
    /// <param name="filePicker">The folder picker service.</param>
    /// <param name="detectManifestKind">The handler backing backup detection.</param>
    private sealed class TestExistingBackupViewModel(
        IQueryHandler<GetSettingsQuery<RecentPathSettings>, RecentPathSettings> recentPathsQuery,
        ICommandHandler<SaveSettingsCommand<RecentPathSettings>, Result> saveRecentPathsCommand,
        IFilePickerService filePicker,
        IQueryHandler<DetectManifestKindQuery, ManifestKind> detectManifestKind
    ) : ExistingBackupViewModelBase(recentPathsQuery, saveRecentPathsCommand, filePicker, detectManifestKind)
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
