using BackupZCrypt.Application.Orchestrators.Interfaces;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Application.ValueObjects.Settings;
using BackupZCrypt.Application.ValueObjects.Manifest;
using BackupZCrypt.Desktop.Resources;
using BackupZCrypt.Desktop.Services.Interfaces;
using BackupZCrypt.Desktop.ViewModels;
using BackupZCrypt.Domain.Services.Interfaces;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.ValueObjects.Backup;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BackupZCrypt.Test.Unit.Desktop;

/// <summary>
/// Unit tests for the pages that work on an existing backup: the manifest detection that decides
/// whether the page asks for a password, the start gate layered on top of it, and the request the
/// restore, update, and verify pages each build from the same inputs. The three pages are nearly
/// identical, so every page-specific assertion here exists to catch a copy-paste between them.
/// </summary>
public sealed class ExistingBackupViewModelTests
{
    /// <summary>
    /// The substituted orchestrator every run is dispatched to.
    /// </summary>
    private readonly IBackupOrchestrator orchestrator = Substitute.For<IBackupOrchestrator>();

    /// <summary>
    /// The substituted settings service the pages read and write the recent paths through.
    /// </summary>
    private readonly ISettingsService settingsService = Substitute.For<ISettingsService>();

    /// <summary>
    /// The substituted folder picker the browse commands go through.
    /// </summary>
    private readonly IFilePickerService filePicker = Substitute.For<IFilePickerService>();

    /// <summary>
    /// The substituted manifest service backing backup detection.
    /// </summary>
    private readonly IManifestService manifestService = Substitute.For<IManifestService>();

    /// <summary>
    /// The synchronization context that was installed before the test, restored afterwards.
    /// </summary>
    private SynchronizationContext? previousContext;

    /// <summary>
    /// Installs a synchronization context that runs posted callbacks inline, so a detection that
    /// completes after its await resumes at a deterministic point in the test instead of on an
    /// arbitrary thread pool thread.
    /// </summary>
    [SetUp]
    public void InstallInlineSynchronizationContext()
    {
        this.previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new InlineSynchronizationContext());
    }

    /// <summary>
    /// Restores the synchronization context so the inline one cannot leak into other fixtures that
    /// share the same thread.
    /// </summary>
    [TearDown]
    public void RestoreSynchronizationContext()
    {
        SynchronizationContext.SetSynchronizationContext(this.previousContext);
    }

    [Test]
    public void BackupPath_WhenAnEncryptedManifestIsFound_AsksForAPasswordAndStartsOnlyOnceItIsTyped()
    {
        StubDetection(ManifestKind.Encrypted);
        var sut = CreateSut();

        sut.DestinationPath = "restored-here";
        sut.SourcePath = "backup";

        var enabledWithoutPassword = sut.StartCommand.CanExecute(null);

        sut.Password = "backup-password";

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sut.IsBackupDetected, Is.True);
            Assert.That(sut.HasDetection, Is.False);
            Assert.That(enabledWithoutPassword, Is.False);
            Assert.That(sut.StartCommand.CanExecute(null), Is.True);
        }
    }

    [Test]
    public void BackupPath_WhenNoManifestIsFound_ShowsTheMissingBackupNoticeAndKeepsTheStartBlocked()
    {
        StubDetection(ManifestKind.Missing);
        var sut = CreateSut();

        sut.DestinationPath = "restored-here";
        sut.SourcePath = "not-a-backup";
        sut.Password = "backup-password";

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sut.IsBackupDetected, Is.False);
            Assert.That(sut.HasDetection, Is.True);
            Assert.That(sut.StartCommand.CanExecute(null), Is.False);
        }
    }

    [TestCaseSource(nameof(ProbeFailures))]
    public void BackupPath_WhenTheProbeFails_IsReportedAsNoBackupFoundRatherThanLeftUndecided(
        Exception failure
    )
    {
        _ = this
            .manifestService.DetectManifestKindAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Throws(failure);

        var sut = CreateSut();

        sut.DestinationPath = "restored-here";
        sut.SourcePath = "unreadable-backup";
        sut.Password = "backup-password";

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sut.IsBackupDetected, Is.False);
            Assert.That(sut.HasDetection, Is.True);
            Assert.That(sut.StartCommand.CanExecute(null), Is.False);
        }
    }

    [Test]
    public void BackupPath_WhenClearedAfterADetection_ForgetsThePasswordRequirementAndTheNotice()
    {
        StubDetection(ManifestKind.Encrypted);
        var sut = CreateSut();

        sut.DestinationPath = "restored-here";
        sut.SourcePath = "backup";
        sut.Password = "backup-password";
        var enabledWithABackup = sut.StartCommand.CanExecute(null);

        sut.SourcePath = "   ";

        using (Assert.EnterMultipleScope())
        {
            Assert.That(enabledWithABackup, Is.True);
            Assert.That(sut.IsBackupDetected, Is.False);
            Assert.That(sut.HasDetection, Is.False);
            Assert.That(sut.StartCommand.CanExecute(null), Is.False);
        }
    }

    [Test]
    public void BackupPath_WhenAnEarlierProbeCompletesLate_KeepsTheDetectionOfTheCurrentPath()
    {
        TaskCompletionSource<ManifestKind> slowProbe = new();

        _ = this
            .manifestService.DetectManifestKindAsync(
                "slow-backup",
                Arg.Any<CancellationToken>()
            )
            .Returns(slowProbe.Task);
        _ = this
            .manifestService.DetectManifestKindAsync(
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

        using (Assert.EnterMultipleScope())
        {
            Assert.That(detectedBeforeTheStaleResult, Is.False);
            Assert.That(sut.IsBackupDetected, Is.False);
            Assert.That(sut.HasDetection, Is.True);
            Assert.That(sut.StartCommand.CanExecute(null), Is.False);
        }
    }

    [Test]
    public void BackupPath_WhenTheDetectionFinishesAfterThePathWasSet_ReEvaluatesTheStartCommand()
    {
        TaskCompletionSource<ManifestKind> probe = new();

        _ = this
            .manifestService.DetectManifestKindAsync(
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

        using (Assert.EnterMultipleScope())
        {
            Assert.That(enabledWhileProbing, Is.False);
            Assert.That(notifications, Is.GreaterThan(notificationsWhileProbing));
            Assert.That(sut.StartCommand.CanExecute(null), Is.True);
        }
    }

    [TestCaseSource(nameof(PageRequestCases))]
    public async Task StartCommand_OnEachExistingBackupPage_SendsThatPagesOperationAndSuccessTitle(
        BackupOperation operation,
        string expectedDestination,
        string expectedSuccessTitle
    )
    {
        StubDetection(ManifestKind.Encrypted);
        var requests = StubOrchestratorCapturingRequests();
        var sut = CreatePage(operation);

        sut.SourcePath = "backup-source";
        sut.DestinationPath = "backup-destination";
        sut.Password = "backup-password";

        await sut.StartCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(requests, Has.Count.EqualTo(1));
            Assert.That(requests[0].Operation, Is.EqualTo(operation));
            Assert.That(requests[0].SourcePath, Is.EqualTo("backup-source"));
            Assert.That(requests[0].DestinationPath, Is.EqualTo(expectedDestination));
            Assert.That(requests[0].Password, Is.EqualTo("backup-password"));
            Assert.That(requests[0].ConfirmPassword, Is.EqualTo("backup-password"));
            Assert.That(requests[0].Compression, Is.EqualTo(CompressionMode.None));
            Assert.That(requests[0].ProceedOnWarnings, Is.False);
            Assert.That(sut.ResultIsSuccess, Is.True);
            Assert.That(sut.ResultTitle, Is.EqualTo(expectedSuccessTitle));
        }
    }

    [TestCase(BackupOperation.Restore, "remembered-destination", "")]
    [TestCase(BackupOperation.Update, "remembered-source", "remembered-destination")]
    [TestCase(BackupOperation.Verify, "remembered-destination", "")]
    public async Task OnNavigatedToAsync_OnEachExistingBackupPage_SeedsThePathsItActuallyOperatesOn(
        BackupOperation operation,
        string expectedSource,
        string expectedDestination
    )
    {
        StubDetection(ManifestKind.Encrypted);
        var sut = CreatePage(operation);

        await sut.OnNavigatedToAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sut.SourcePath, Is.EqualTo(expectedSource));
            Assert.That(sut.DestinationPath, Is.EqualTo(expectedDestination));
        }
    }

    [TestCase(BackupOperation.Restore, "backup-source", "backup-destination")]
    [TestCase(BackupOperation.Update, "backup-source", "backup-destination")]
    [TestCase(BackupOperation.Verify, "remembered-source", "backup-source")]
    public async Task StartCommand_WhenThePageSucceeds_RemembersOnlyThePathsThatPageOwns(
        BackupOperation operation,
        string expectedLastSource,
        string expectedLastDestination
    )
    {
        StubDetection(ManifestKind.Encrypted);
        _ = StubOrchestratorCapturingRequests();

        List<RecentPathSettings> saved = [];
        _ = this
            .settingsService.SaveAsync(
                Arg.Do<RecentPathSettings>(saved.Add),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.CompletedTask);

        var sut = CreatePage(operation);
        sut.SourcePath = "backup-source";
        sut.DestinationPath = "backup-destination";
        sut.Password = "backup-password";

        await sut.StartCommand.ExecuteAsync(null);

        RecentPathSettings[] expected = [new(expectedLastSource, expectedLastDestination)];

        Assert.That(saved, Is.EqualTo(expected));
    }

    [TestCase(BackupOperation.Restore, false)]
    [TestCase(BackupOperation.Update, false)]
    [TestCase(BackupOperation.Verify, true)]
    public void StartCommand_WithoutADestination_IsEnabledOnlyOnThePageThatWritesNothing(
        BackupOperation operation,
        bool expected
    )
    {
        StubDetection(ManifestKind.Encrypted);
        var sut = CreatePage(operation);

        sut.SourcePath = "backup-source";
        sut.Password = "backup-password";

        Assert.That(sut.StartCommand.CanExecute(null), Is.EqualTo(expected));
    }

    [Test]
    public async Task PickCommands_OnTheRestorePage_PutTheBackupInTheSourceAndTheRecoveryInTheDestination()
    {
        StubDetection(ManifestKind.Encrypted);
        StubPickedFolders("picked-backup", "picked-recovery");
        StubRememberedPaths();

        RestoreBackupViewModel sut = new(
            this.orchestrator,
            this.settingsService,
            this.filePicker,
            this.manifestService
        );

        await sut.PickBackupFolderCommand.ExecuteAsync(null);
        await sut.PickDestinationFolderCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sut.SourcePath, Is.EqualTo("picked-backup"));
            Assert.That(sut.DestinationPath, Is.EqualTo("picked-recovery"));
        }
    }

    [Test]
    public async Task PickCommands_OnTheUpdatePage_PutTheBackupInTheDestinationAndTheScannedFolderInTheSource()
    {
        StubDetection(ManifestKind.Encrypted);
        StubPickedFolders("picked-scan", "picked-backup");
        StubRememberedPaths();

        UpdateBackupViewModel sut = new(
            this.orchestrator,
            this.settingsService,
            this.filePicker,
            this.manifestService
        );

        await sut.PickSourceFolderCommand.ExecuteAsync(null);
        await sut.PickBackupFolderCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sut.SourcePath, Is.EqualTo("picked-scan"));
            Assert.That(sut.DestinationPath, Is.EqualTo("picked-backup"));
        }
    }

    [Test]
    public async Task PickCommand_OnTheVerifyPage_FillsTheBackupPathAndKeepsItWhenTheDialogIsDismissed()
    {
        StubDetection(ManifestKind.Encrypted);
        StubPickedFolders("picked-backup", null);
        StubRememberedPaths();

        VerifyBackupViewModel sut = new(
            this.orchestrator,
            this.settingsService,
            this.filePicker,
            this.manifestService
        );

        await sut.PickBackupFolderCommand.ExecuteAsync(null);
        var pathAfterPicking = sut.SourcePath;

        await sut.PickBackupFolderCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pathAfterPicking, Is.EqualTo("picked-backup"));
            Assert.That(sut.SourcePath, Is.EqualTo("picked-backup"));
            Assert.That(sut.DestinationPath, Is.Empty);
        }
    }

    /// <summary>
    /// The failures a manifest probe can realistically raise. Detection only decides what the page
    /// shows, so each of them must degrade to "no backup found" rather than leave the page in the
    /// state of the previously inspected path.
    /// </summary>
    /// <returns>One case per probe failure.</returns>
    private static IEnumerable<TestCaseData> ProbeFailures()
    {
        yield return new TestCaseData(new IOException("backup unreadable"));
        yield return new TestCaseData(new UnauthorizedAccessException("access denied"));
        yield return new TestCaseData(new OperationCanceledException());
    }

    /// <summary>
    /// The existing-backup pages paired with the destination each one is expected to put in its
    /// request and the title it reports on success.
    /// </summary>
    /// <returns>One case per page.</returns>
    private static IEnumerable<TestCaseData> PageRequestCases()
    {
        yield return new TestCaseData(
            BackupOperation.Restore,
            "backup-destination",
            Strings.ResultSuccessTitle
        );
        yield return new TestCaseData(
            BackupOperation.Update,
            "backup-destination",
            Strings.ResultSuccessTitle
        );
        yield return new TestCaseData(
            BackupOperation.Verify,
            string.Empty,
            Strings.VerifySuccessTitle
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
            this.orchestrator,
            this.settingsService,
            this.filePicker,
            this.manifestService
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
                this.orchestrator,
                this.settingsService,
                this.filePicker,
                this.manifestService
            ),
            BackupOperation.Update => new UpdateBackupViewModel(
                this.orchestrator,
                this.settingsService,
                this.filePicker,
                this.manifestService
            ),
            BackupOperation.Verify => new VerifyBackupViewModel(
                this.orchestrator,
                this.settingsService,
                this.filePicker,
                this.manifestService
            ),
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
            .manifestService.DetectManifestKindAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(kind));
    }

    /// <summary>
    /// Stubs the remembered paths, since an unstubbed settings read hands the page a
    /// <see langword="null"/> settings object.
    /// </summary>
    private void StubRememberedPaths()
    {
        _ = this
            .settingsService.GetOrCreateAsync<RecentPathSettings>(Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(
                    new RecentPathSettings("remembered-source", "remembered-destination")
                )
            );
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
    /// Makes the orchestrator report a trivial success and records every request it receives.
    /// </summary>
    /// <returns>The list the captured requests are appended to.</returns>
    private List<BackupRequest> StubOrchestratorCapturingRequests()
    {
        List<BackupRequest> requests = [];

        _ = this
            .orchestrator.ExecuteAsync(
                Arg.Do<BackupRequest>(requests.Add),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Task.FromResult(
                    Result<BackupResult>.Success(
                        new BackupResult(true, TimeSpan.FromSeconds(1), 16, 1, 1)
                    )
                )
            );

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
    /// the source path, as it does on the restore and verify pages.
    /// </summary>
    /// <param name="orchestrator">The orchestrator that executes the operation.</param>
    /// <param name="settingsService">The service that reads and persists the recent paths.</param>
    /// <param name="filePicker">The folder picker service.</param>
    /// <param name="manifestService">The service backing backup detection.</param>
    private sealed class TestExistingBackupViewModel(
        IBackupOrchestrator orchestrator,
        ISettingsService settingsService,
        IFilePickerService filePicker,
        IManifestService manifestService
    ) : ExistingBackupViewModelBase(orchestrator, settingsService, filePicker, manifestService)
    {
        /// <inheritdoc/>
        protected override string BackupPath => SourcePath;

        /// <inheritdoc/>
        protected override BackupRequest CreateRequest(bool proceedOnWarnings)
        {
            return new BackupRequest(
                SourcePath,
                DestinationPath,
                Password,
                Password,
                EncryptionAlgorithm.Aes,
                KeyDerivationAlgorithm.PBKDF2,
                BackupOperation.Restore,
                CompressionMode.None,
                proceedOnWarnings
            );
        }
    }
}
