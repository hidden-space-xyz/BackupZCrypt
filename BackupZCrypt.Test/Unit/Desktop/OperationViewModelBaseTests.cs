using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using BackupZCrypt.Application.Commands;
using BackupZCrypt.Application.Commands.Interfaces;
using BackupZCrypt.Application.Queries;
using BackupZCrypt.Application.Queries.Interfaces;
using BackupZCrypt.Application.Utilities.Formatters;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Application.ValueObjects.Backup;
using BackupZCrypt.Application.ValueObjects.Settings;
using BackupZCrypt.Desktop.Resources;
using BackupZCrypt.Desktop.Services;
using BackupZCrypt.Desktop.Services.Interfaces;
using BackupZCrypt.Desktop.ViewModels;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.ValueObjects.Backup;
using BackupZCrypt.Domain.ValueObjects.Localization;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BackupZCrypt.Test.Unit.Desktop;

/// <summary>
/// Unit tests for the shared operation engine behind the create, update, restore, and verify pages:
/// the start gate, the progress projection, cancellation, the warnings confirmation flow, the result
/// panel, and the recent-path bookkeeping.
/// </summary>
public sealed class OperationViewModelBaseTests
{
    /// <summary>
    /// The substituted handler every run of the test page is dispatched to.
    /// </summary>
    private readonly ICommandHandler<CreateBackupCommand, Result<BackupOutcome>> createBackup =
        Substitute.For<ICommandHandler<CreateBackupCommand, Result<BackupOutcome>>>();

    /// <summary>
    /// The substituted handler the page reads the recent paths through.
    /// </summary>
    private readonly IQueryHandler<GetSettingsQuery<RecentPathSettings>, RecentPathSettings> recentPathsQuery =
        Substitute.For<IQueryHandler<GetSettingsQuery<RecentPathSettings>, RecentPathSettings>>();

    /// <summary>
    /// The substituted handler the page persists the recent paths through.
    /// </summary>
    private readonly ICommandHandler<SaveSettingsCommand<RecentPathSettings>, Result> saveRecentPaths =
        Substitute.For<ICommandHandler<SaveSettingsCommand<RecentPathSettings>, Result>>();

    /// <summary>
    /// The substituted folder picker, never exercised here but required by the constructor.
    /// </summary>
    private readonly IFilePickerService filePicker = Substitute.For<IFilePickerService>();

    /// <summary>
    /// The synchronization context that was installed before the test, restored afterwards.
    /// </summary>
    private SynchronizationContext? previousContext;

    /// <summary>
    /// Installs a synchronization context that runs posted callbacks inline, so the
    /// <see cref="Progress{T}"/> the page attaches to its message delivers its reports
    /// synchronously instead of on an arbitrary thread pool thread.
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

    [TestCase("", "", false)]
    [TestCase("source", "", false)]
    [TestCase("", "destination", false)]
    [TestCase("   ", "destination", false)]
    [TestCase("source", "   ", false)]
    [TestCase("source", "destination", true)]
    public void StartCommand_CanExecute_RequiresBothPathsToBeNonBlank(
        string source,
        string destination,
        bool expected
    )
    {
        var sut = CreateSut();

        sut.SourcePath = source;
        sut.DestinationPath = destination;

        Assert.That(sut.StartCommand.CanExecute(null), Is.EqualTo(expected));
    }

    [Test]
    public void StartCommand_WhileAnOperationRuns_IsBlockedAndBecomesAvailableAgainWhenItEnds()
    {
        var sut = CreateSut();
        sut.SourcePath = "source";
        sut.DestinationPath = "destination";

        sut.IsRunning = true;
        Assert.That(sut.StartCommand.CanExecute(null), Is.False);

        sut.IsRunning = false;
        Assert.That(sut.StartCommand.CanExecute(null), Is.True);
    }

    [TestCase(true, 5)]
    [TestCase(false, 3)]
    public async Task StartCommand_WhenTheOperationCompletes_ShowsTheResultPanelMatchingTheOutcome(
        bool succeeded,
        int processedFiles
    )
    {
        var sut = CreateSut();
        StubHandler(
            Result<BackupOutcome>.Success(
                BackupOutcome.Completed(
                    new BackupResult(succeeded, TimeSpan.FromMinutes(3), 2048, processedFiles, 5)
                )
            )
        );

        sut.SourcePath = "source";
        sut.DestinationPath = "destination";

        await sut.StartCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sut.HasResult, Is.True);
            Assert.That(sut.HasResultDetails, Is.True);
            Assert.That(sut.ResultIsSuccess, Is.EqualTo(succeeded));
            Assert.That(
                sut.ResultTitle,
                Is.EqualTo(succeeded ? Strings.ResultSuccessTitle : Strings.ResultPartialTitle)
            );
            Assert.That(
                sut.ResultFiles,
                Is.EqualTo(
                    string.Format(
                        CultureInfo.CurrentCulture,
                        Strings.ResultFilesFormat,
                        processedFiles,
                        5
                    )
                )
            );
            Assert.That(sut.ResultDuration, Does.Contain("00:03:00"));
            Assert.That(sut.ResultSize, Does.Contain(ByteSizeFormatter.Format(2048)));
            Assert.That(sut.ShowErrors, Is.False);
            Assert.That(sut.ShowWarnings, Is.False);
            Assert.That(sut.IsRunning, Is.False);
        }
    }

    [Test]
    public async Task StartCommand_WhenTheOutcomeAwaitsConfirmation_OpensTheConfirmationGate()
    {
        var sut = CreateSut();
        LocalizableMessage warning = new(MessageCode.DestinationExistingFilesFormat, 12);
        StubHandler(Result<BackupOutcome>.Success(BackupOutcome.AwaitingConfirmation([warning])));

        sut.SourcePath = "source";
        sut.DestinationPath = "destination";

        await sut.StartCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sut.ShowWarnings, Is.True);
            Assert.That(sut.HasResult, Is.False);
            Assert.That(sut.Warnings, Is.EqualTo([MessageLocalizer.Localize(warning)]));
        }
    }

    [Test]
    public async Task StartCommand_WhenACompletedRunCarriesWarnings_ShowsTheResultInsteadOfTheGate()
    {
        var sut = CreateSut();
        StubHandler(
            Result<BackupOutcome>.Success(
                BackupOutcome.Completed(
                    new BackupResult(
                        false,
                        TimeSpan.FromSeconds(4),
                        2048,
                        3,
                        5,
                        warnings: [new LocalizableMessage(MessageCode.DestinationExistingFilesFormat, 12)]
                    )
                )
            )
        );

        sut.SourcePath = "source";
        sut.DestinationPath = "destination";

        await sut.StartCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sut.ShowWarnings, Is.False);
            Assert.That(sut.HasResult, Is.True);
            Assert.That(sut.ResultTitle, Is.EqualTo(Strings.ResultPartialTitle));
        }
    }

    [Test]
    public async Task ContinueAnywayCommand_ReRunsTheMessageWithProceedOnWarningsSet()
    {
        List<CreateBackupCommand> commands = [];
        var sut = CreateSut();

        _ = this
            .createBackup.HandleAsync(
                Arg.Do<CreateBackupCommand>(commands.Add),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Task.FromResult(
                    Result<BackupOutcome>.Success(
                        BackupOutcome.AwaitingConfirmation(
                            [new LocalizableMessage(MessageCode.DestinationExistingFilesFormat, 12)]
                        )
                    )
                ),
                Task.FromResult(
                    Result<BackupOutcome>.Success(
                        BackupOutcome.Completed(
                            new BackupResult(true, TimeSpan.FromSeconds(1), 16, 2, 2)
                        )
                    )
                )
            );

        sut.SourcePath = "source";
        sut.DestinationPath = "destination";

        await sut.StartCommand.ExecuteAsync(null);
        var gateOpened = sut.ShowWarnings;

        await sut.ContinueAnywayCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(gateOpened, Is.True);
            Assert.That(
                commands.Select(static command => command.ProceedOnWarnings),
                Is.EqualTo([false, true])
            );
            Assert.That(sut.ShowWarnings, Is.False);
            Assert.That(sut.Warnings, Is.Empty);
            Assert.That(sut.HasResult, Is.True);
            Assert.That(sut.ResultIsSuccess, Is.True);
        }
    }

    [Test]
    public async Task CancelOperationCommand_CancelsTheTokenTheHandlerReceived_AndLeavesThePageIdle()
    {
        var sut = CreateSut();

        _ = this
            .createBackup.HandleAsync(Arg.Any<CreateBackupCommand>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var token = callInfo.Arg<CancellationToken>();
                sut.CancelOperationCommand.Execute(null);
                token.ThrowIfCancellationRequested();

                return Task.FromResult(
                    Result<BackupOutcome>.Success(
                        BackupOutcome.Completed(new BackupResult(true, TimeSpan.Zero, 0, 0, 0))
                    )
                );
            });

        sut.SourcePath = "source";
        sut.DestinationPath = "destination";

        await sut.StartCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sut.HasResult, Is.True);
            Assert.That(sut.ResultIsSuccess, Is.False);
            Assert.That(sut.ResultTitle, Is.EqualTo(Strings.ResultCancelled));
            Assert.That(sut.ShowErrors, Is.False);
            Assert.That(sut.IsRunning, Is.False);
            Assert.That(sut.StartCommand.CanExecute(null), Is.True);
        }

        Assert.That(() => sut.CancelOperationCommand.Execute(null), Throws.Nothing);
    }

    [Test]
    public async Task StartCommand_WhenTheHandlerThrows_ShowsTheMessageAndReturnsToIdle()
    {
        var sut = CreateSut();
        _ = this
            .createBackup.HandleAsync(Arg.Any<CreateBackupCommand>(), Arg.Any<CancellationToken>())
            .Throws(new IOException("disk gone"));

        sut.SourcePath = "source";
        sut.DestinationPath = "destination";

        await sut.StartCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sut.HasResult, Is.True);
            Assert.That(sut.ResultIsSuccess, Is.False);
            Assert.That(sut.ResultTitle, Is.EqualTo(Strings.ResultErrorTitle));
            Assert.That(
                sut.Errors,
                Has.Count.EqualTo(1).And.All.Contains("disk gone"),
                "the exception detail must survive"
            );
            Assert.That(
                sut.Errors[0],
                Is.Not.EqualTo("disk gone"),
                "the detail must be wrapped in the localized frame rather than shown bare, so the "
                    + "sentence around it follows the user's language"
            );
            Assert.That(sut.ShowErrors, Is.True);
            Assert.That(sut.IsRunning, Is.False);
            Assert.That(sut.StartCommand.CanExecute(null), Is.True);
        }
    }

    [Test]
    [SuppressMessage(
        "Usage",
        "CA2201:Do not raise reserved exception types",
        Justification = "Throwing a runtime-reserved exception is the point of the test: it proves "
            + "that fatal exceptions like OutOfMemoryException escape the page's catch instead of "
            + "being swallowed and reported as an ordinary failure."
    )]
    public void StartCommand_WhenTheHandlerRunsOutOfMemory_LetsItEscapeAndStillClearsTheRunningState()
    {
        var sut = CreateSut();
        _ = this
            .createBackup.HandleAsync(Arg.Any<CreateBackupCommand>(), Arg.Any<CancellationToken>())
            .Throws(new OutOfMemoryException());

        sut.SourcePath = "source";
        sut.DestinationPath = "destination";

        _ = Assert.ThrowsAsync<OutOfMemoryException>(() => sut.StartCommand.ExecuteAsync(null));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sut.IsRunning, Is.False);
            Assert.That(sut.HasResult, Is.False);
        }
    }

    [Test]
    public async Task StartCommand_WhenTheEngineReportsProgress_ProjectsBytesOntoThePercentAndCaptions()
    {
        var sut = CreateSut();
        var indeterminateWhileScanning = false;
        var percentWhileScanning = -1d;

        _ = this
            .createBackup.HandleAsync(Arg.Any<CreateBackupCommand>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var command =
                    callInfo.Arg<CreateBackupCommand>()
                    ?? throw new InvalidOperationException(
                        "The handler was invoked without a command."
                    );
                var progress =
                    command.Progress
                    ?? throw new InvalidOperationException(
                        "The handler received a command without a progress reporter."
                    );

                progress.Report(new BackupStatus(0, 10, 0, 0, TimeSpan.Zero));
                indeterminateWhileScanning = sut.IsProgressIndeterminate;
                percentWhileScanning = sut.ProgressValue;

                progress.Report(
                    new BackupStatus(4, 10, 250, 1000, TimeSpan.FromSeconds(3725))
                );

                return Task.FromResult(
                    Result<BackupOutcome>.Success(
                        BackupOutcome.Completed(
                            new BackupResult(true, TimeSpan.FromSeconds(3725), 1000, 10, 10)
                        )
                    )
                );
            });

        sut.SourcePath = "source";
        sut.DestinationPath = "destination";

        await sut.StartCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(indeterminateWhileScanning, Is.True);
            Assert.That(percentWhileScanning, Is.Zero);
            Assert.That(sut.IsProgressIndeterminate, Is.False);
            Assert.That(sut.ProgressValue, Is.EqualTo(25d).Within(0.001));
            Assert.That(
                sut.ProgressText,
                Is.EqualTo(
                    string.Format(CultureInfo.CurrentCulture, Strings.ProgressFilesFormat, 4, 10)
                )
            );
            Assert.That(sut.ElapsedText, Does.Contain("01:02:05"));
        }
    }

    [Test]
    public async Task StartCommand_RunAgain_ClearsThePreviousRunsErrorsAndProgress()
    {
        var sut = CreateSut();

        _ = this
            .createBackup.HandleAsync(Arg.Any<CreateBackupCommand>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(
                    Result<BackupOutcome>.Failure(
                        new LocalizableMessage(MessageCode.SourceAccessDenied),
                        new LocalizableMessage(MessageCode.PasswordTooShort)
                    )
                ),
                Task.FromResult(
                    Result<BackupOutcome>.Success(
                        BackupOutcome.Completed(
                            new BackupResult(true, TimeSpan.FromSeconds(1), 16, 2, 2)
                        )
                    )
                )
            );

        sut.SourcePath = "source";
        sut.DestinationPath = "destination";

        await sut.StartCommand.ExecuteAsync(null);
        var errorsAfterFirstRun = sut.Errors.Count;

        await sut.StartCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(errorsAfterFirstRun, Is.EqualTo(2));
            Assert.That(sut.Errors, Is.Empty);
            Assert.That(sut.Warnings, Is.Empty);
            Assert.That(sut.ShowErrors, Is.False);
            Assert.That(sut.ShowWarnings, Is.False);
            Assert.That(sut.ResultIsSuccess, Is.True);
            Assert.That(sut.ResultTitle, Is.EqualTo(Strings.ResultSuccessTitle));
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task StartCommand_PersistsTheUsedPathsOnlyWhenTheOperationSucceeded(bool succeeded)
    {
        List<RecentPathSettings> saved = [];
        var sut = CreateSut();

        _ = this
            .saveRecentPaths.HandleAsync(
                Arg.Do<SaveSettingsCommand<RecentPathSettings>>(command => saved.Add(command.Settings)),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());

        StubHandler(
            Result<BackupOutcome>.Success(
                BackupOutcome.Completed(
                    new BackupResult(succeeded, TimeSpan.FromSeconds(1), 16, succeeded ? 2 : 1, 2)
                )
            )
        );

        sut.SourcePath = "chosen-source";
        sut.DestinationPath = "chosen-destination";

        await sut.StartCommand.ExecuteAsync(null);

        RecentPathSettings[] expected = succeeded
            ? [new RecentPathSettings("chosen-source", "chosen-destination")]
            : [];

        Assert.That(saved, Is.EqualTo(expected));
    }

    [Test]
    public async Task OnNavigatedToAsync_CalledAgain_KeepsWhatTheUserTypedInsteadOfReapplyingRecentPaths()
    {
        var sut = CreateSut();

        await sut.OnNavigatedToAsync();
        var sourceAfterFirstVisit = sut.SourcePath;
        var destinationAfterFirstVisit = sut.DestinationPath;

        sut.DestinationPath = "typed-by-the-user";
        await sut.OnNavigatedToAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sourceAfterFirstVisit, Is.EqualTo("remembered-source"));
            Assert.That(destinationAfterFirstVisit, Is.EqualTo("remembered-destination"));
            Assert.That(sut.DestinationPath, Is.EqualTo("typed-by-the-user"));
            Assert.That(sut.AppliedRecentPaths, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task OnNavigatedToAsync_WhenTheHandlerReportsTheDefaults_LeavesThePageUsable()
    {
        var sut = CreateSut();
        _ = this
            .recentPathsQuery.HandleAsync(
                Arg.Any<GetSettingsQuery<RecentPathSettings>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(RecentPathSettings.DefaultValue);

        await sut.OnNavigatedToAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sut.SourcePath, Is.Empty);
            Assert.That(sut.DestinationPath, Is.Empty);
            Assert.That(sut.AppliedRecentPaths, Is.EqualTo([RecentPathSettings.DefaultValue]));
        }
    }

    [TestCase(true, 5, 5, "", Description = "cleared once the operation it protected has succeeded")]
    [TestCase(false, 3, 5, "secret", Description = "kept after a partial run, which may be re-run")]
    public async Task StartCommand_ClearsThePasswordOnlyWhenTheOperationSucceeded(
        bool succeeded,
        int processedFiles,
        int totalFiles,
        string expectedPassword
    )
    {
        var sut = CreateSut();
        StubHandler(
            Result<BackupOutcome>.Success(
                BackupOutcome.Completed(
                    new BackupResult(
                        succeeded,
                        TimeSpan.FromSeconds(1),
                        1024,
                        processedFiles,
                        totalFiles
                    )
                )
            )
        );

        sut.SourcePath = "source";
        sut.DestinationPath = "destination";
        sut.Password = "secret";

        await sut.StartCommand.ExecuteAsync(null);

        Assert.That(
            sut.Password,
            Is.EqualTo(expectedPassword),
            "Page ViewModels are singletons held for the life of the window, so a password kept after "
                + "a successful run stays reachable forever; clearing after a failed one would force "
                + "the user to retype a generated password to correct an unrelated problem."
        );
    }

    [Test]
    public async Task StartCommand_CapturesThePasswordBeforeClearingIt()
    {
        List<CreateBackupCommand> commands = [];
        var sut = CreateSut();

        _ = this
            .createBackup.HandleAsync(
                Arg.Do<CreateBackupCommand>(commands.Add),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Task.FromResult(
                    Result<BackupOutcome>.Success(
                        BackupOutcome.Completed(
                            new BackupResult(true, TimeSpan.FromSeconds(1), 1024, 1, 1)
                        )
                    )
                )
            );

        sut.SourcePath = "source";
        sut.DestinationPath = "destination";
        sut.Password = "secret";

        await sut.StartCommand.ExecuteAsync(null);

        Assert.That(
            commands.Select(static command => command.Password),
            Is.EqualTo(["secret"]),
            "The message must carry the real password: clearing it before the message was built "
                + "would silently send an empty one to the engine."
        );
    }

    /// <summary>
    /// Builds the concrete test page with the remembered paths already stubbed, since an unstubbed
    /// handler hands the page a <see langword="null"/> settings object.
    /// </summary>
    /// <returns>The system under test.</returns>
    private TestOperationViewModel CreateSut()
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

        return new TestOperationViewModel(
            this.createBackup,
            this.recentPathsQuery,
            this.saveRecentPaths,
            this.filePicker
        );
    }

    /// <summary>
    /// Makes every handler call return the same outcome.
    /// </summary>
    /// <param name="result">The outcome the handler reports.</param>
    private void StubHandler(Result<BackupOutcome> result)
    {
        _ = this
            .createBackup.HandleAsync(Arg.Any<CreateBackupCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(result));
    }

    /// <summary>
    /// A synchronization context that invokes posted callbacks on the calling thread, making the
    /// progress reports the engine pushes observable at a deterministic point in the test.
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
    /// The minimal concrete page used to exercise the abstract engine: it builds a create command
    /// from the current inputs and records every recent-path application so the load-once guard is
    /// observable.
    /// </summary>
    /// <param name="createBackup">The handler that executes the operation.</param>
    /// <param name="recentPathsQuery">The handler that loads the recently used paths.</param>
    /// <param name="saveRecentPathsCommand">The handler that persists the recently used paths.</param>
    /// <param name="filePicker">The folder picker service.</param>
    private sealed class TestOperationViewModel(
        ICommandHandler<CreateBackupCommand, Result<BackupOutcome>> createBackup,
        IQueryHandler<GetSettingsQuery<RecentPathSettings>, RecentPathSettings> recentPathsQuery,
        ICommandHandler<SaveSettingsCommand<RecentPathSettings>, Result> saveRecentPathsCommand,
        IFilePickerService filePicker
    ) : OperationViewModelBase(recentPathsQuery, saveRecentPathsCommand, filePicker)
    {
        /// <summary>
        /// Gets the recent-path settings handed to the page, one entry per application.
        /// </summary>
        public List<RecentPathSettings> AppliedRecentPaths { get; } = [];

        /// <inheritdoc/>
        /// <remarks>
        /// Reads the real <c>Password</c> rather than a literal, so a change that cleared it before
        /// the message was built shows up here instead of hiding behind a stub value.
        /// </remarks>
        protected override Task<Result<BackupOutcome>> ExecuteOperationAsync(
            bool proceedOnWarnings,
            IProgress<BackupStatus> progress,
            CancellationToken cancellationToken
        )
        {
            var command = new CreateBackupCommand(
                SourcePath,
                DestinationPath,
                Password,
                Password,
                EncryptionAlgorithm.Aes,
                KeyDerivationAlgorithm.PBKDF2,
                CompressionMode.None,
                proceedOnWarnings
            )
            {
                Progress = progress,
            };

            return createBackup.HandleAsync(command, cancellationToken);
        }

        /// <inheritdoc/>
        protected override void ApplyRecentPaths(RecentPathSettings recent)
        {
            AppliedRecentPaths.Add(recent);

            SourcePath = recent.LastSourcePath ?? SourcePath;
            DestinationPath = recent.LastDestinationPath ?? DestinationPath;
        }
    }
}
