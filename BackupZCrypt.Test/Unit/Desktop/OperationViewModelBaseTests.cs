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
public sealed class OperationViewModelBaseTests : IDisposable
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
    private readonly SynchronizationContext? previousContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="OperationViewModelBaseTests"/> class, installing
    /// a synchronization context that runs posted callbacks inline, so the <see cref="Progress{T}"/>
    /// the page attaches to its message delivers its reports synchronously instead of on an
    /// arbitrary thread pool thread. A fresh instance is constructed for every test, so this is the
    /// per-test setup hook.
    /// </summary>
    public OperationViewModelBaseTests()
    {
        this.previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new InlineSynchronizationContext());
    }

    /// <summary>
    /// Restores the synchronization context so the inline one cannot leak into other fixtures that
    /// share the same thread.
    /// </summary>
    public void Dispose()
    {
        SynchronizationContext.SetSynchronizationContext(this.previousContext);
    }

    [Theory]
    [InlineData("", "", false)]
    [InlineData("source", "", false)]
    [InlineData("", "destination", false)]
    [InlineData("   ", "destination", false)]
    [InlineData("source", "   ", false)]
    [InlineData("source", "destination", true)]
    internal void StartCommand_CanExecute_RequiresBothPathsToBeNonBlank(
        string source,
        string destination,
        bool expected
    )
    {
        var sut = CreateSut();

        sut.SourcePath = source;
        sut.DestinationPath = destination;

        Assert.Equal(expected, sut.StartCommand.CanExecute(null));
    }

    [Fact]
    internal void StartCommand_WhileAnOperationRuns_IsBlockedAndBecomesAvailableAgainWhenItEnds()
    {
        var sut = CreateSut();
        sut.SourcePath = "source";
        sut.DestinationPath = "destination";

        sut.IsRunning = true;
        Assert.False(sut.StartCommand.CanExecute(null));

        sut.IsRunning = false;
        Assert.True(sut.StartCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(true, 5)]
    [InlineData(false, 3)]
    internal async Task StartCommand_WhenTheOperationCompletes_ShowsTheResultPanelMatchingTheOutcome(
        bool succeeded,
        int processedFiles
    )
    {
        var sut = CreateSut();
        StubHandler(
            Result<BackupOutcome>.Success(
                BackupOutcome.Completed(
                    new BackupResult(TimeSpan.FromMinutes(3), 2048, processedFiles, 5)
                )
            )
        );

        sut.SourcePath = "source";
        sut.DestinationPath = "destination";

        await sut.StartCommand.ExecuteAsync(null);

        Assert.Multiple(
            () => Assert.True(sut.HasResult),
            () => Assert.True(sut.HasResultDetails),
            () => Assert.Equal(succeeded, sut.ResultIsSuccess),
            () =>
                Assert.Equal(
                    succeeded ? Strings.ResultSuccessTitle : Strings.ResultPartialTitle,
                    sut.ResultTitle
                ),
            () =>
                Assert.Equal(
                    string.Format(
                        CultureInfo.CurrentCulture,
                        Strings.ResultFilesFormat,
                        processedFiles,
                        5
                    ),
                    sut.ResultFiles
                ),
            () => Assert.Contains("00:03:00", sut.ResultDuration, StringComparison.Ordinal),
            () =>
                Assert.Contains(
                    ByteSizeFormatter.Format(2048),
                    sut.ResultSize,
                    StringComparison.Ordinal
                ),
            () => Assert.False(sut.ShowErrors),
            () => Assert.False(sut.ShowWarnings),
            () => Assert.False(sut.IsRunning)
        );
    }

    [Fact]
    internal async Task StartCommand_WhenTheOutcomeAwaitsConfirmation_OpensTheConfirmationGate()
    {
        var sut = CreateSut();
        LocalizableMessage warning = new(MessageCode.DestinationExistingFilesFormat, 12);
        StubHandler(Result<BackupOutcome>.Success(BackupOutcome.AwaitingConfirmation([warning])));

        sut.SourcePath = "source";
        sut.DestinationPath = "destination";

        await sut.StartCommand.ExecuteAsync(null);

        Assert.Multiple(
            () => Assert.True(sut.ShowWarnings),
            () => Assert.False(sut.HasResult),
            () => Assert.Equal<string>([MessageLocalizer.Localize(warning)], sut.Warnings)
        );
    }

    [Fact]
    internal async Task StartCommand_WhenACompletedRunCarriesWarnings_ShowsTheResultInsteadOfTheGate()
    {
        var sut = CreateSut();
        StubHandler(
            Result<BackupOutcome>.Success(
                BackupOutcome.Completed(
                    new BackupResult(
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

        Assert.Multiple(
            () => Assert.False(sut.ShowWarnings),
            () => Assert.True(sut.HasResult),
            () => Assert.Equal(Strings.ResultPartialTitle, sut.ResultTitle)
        );
    }

    [Fact]
    internal async Task ContinueAnywayCommand_ReRunsTheMessageWithProceedOnWarningsSet()
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
                            new BackupResult(TimeSpan.FromSeconds(1), 16, 2, 2)
                        )
                    )
                )
            );

        sut.SourcePath = "source";
        sut.DestinationPath = "destination";

        await sut.StartCommand.ExecuteAsync(null);
        var gateOpened = sut.ShowWarnings;

        await sut.ContinueAnywayCommand.ExecuteAsync(null);

        Assert.Multiple(
            () => Assert.True(gateOpened),
            () =>
                Assert.Equal<bool>(
                    [false, true],
                    commands.Select(static command => command.ProceedOnWarnings)
                ),
            () => Assert.False(sut.ShowWarnings),
            () => Assert.Empty(sut.Warnings),
            () => Assert.True(sut.HasResult),
            () => Assert.True(sut.ResultIsSuccess)
        );
    }

    [Fact]
    internal async Task CancelOperationCommand_CancelsTheTokenTheHandlerReceived_AndLeavesThePageIdle()
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
                        BackupOutcome.Completed(new BackupResult(TimeSpan.Zero, 0, 0, 0))
                    )
                );
            });

        sut.SourcePath = "source";
        sut.DestinationPath = "destination";

        await sut.StartCommand.ExecuteAsync(null);

        Assert.Multiple(
            () => Assert.True(sut.HasResult),
            () => Assert.False(sut.ResultIsSuccess),
            () => Assert.Equal(Strings.ResultCancelled, sut.ResultTitle),
            () => Assert.False(sut.ShowErrors),
            () => Assert.False(sut.IsRunning),
            () => Assert.True(sut.StartCommand.CanExecute(null))
        );

        Assert.Null(Record.Exception(() => sut.CancelOperationCommand.Execute(null)));
    }

    [Fact]
    internal async Task StartCommand_WhenTheHandlerThrows_ShowsTheMessageAndReturnsToIdle()
    {
        var sut = CreateSut();
        _ = this
            .createBackup.HandleAsync(Arg.Any<CreateBackupCommand>(), Arg.Any<CancellationToken>())
            .Throws(new IOException("disk gone"));

        sut.SourcePath = "source";
        sut.DestinationPath = "destination";

        await sut.StartCommand.ExecuteAsync(null);

        Assert.Multiple(
            () => Assert.True(sut.HasResult),
            () => Assert.False(sut.ResultIsSuccess),
            () => Assert.Equal(Strings.ResultErrorTitle, sut.ResultTitle),
            () => _ = Assert.Single(sut.Errors),
            () =>
                Assert.All(
                    sut.Errors,
                    error => Assert.Contains("disk gone", error, StringComparison.Ordinal)
                ),
            () => Assert.NotEqual("disk gone", sut.Errors[0]),
            () => Assert.True(sut.ShowErrors),
            () => Assert.False(sut.IsRunning),
            () => Assert.True(sut.StartCommand.CanExecute(null))
        );
    }

    [Fact]
    [SuppressMessage(
        "Usage",
        "CA2201:Do not raise reserved exception types",
        Justification = "Throwing a runtime-reserved exception is the point of the test: it proves "
            + "that fatal exceptions like OutOfMemoryException escape the page's catch instead of "
            + "being swallowed and reported as an ordinary failure."
    )]
    internal async Task StartCommand_WhenTheHandlerRunsOutOfMemory_LetsItEscapeAndStillClearsTheRunningState()
    {
        var sut = CreateSut();
        _ = this
            .createBackup.HandleAsync(Arg.Any<CreateBackupCommand>(), Arg.Any<CancellationToken>())
            .Throws(new OutOfMemoryException());

        sut.SourcePath = "source";
        sut.DestinationPath = "destination";

        _ = await Assert.ThrowsAsync<OutOfMemoryException>(
            () => sut.StartCommand.ExecuteAsync(null)
        );

        Assert.Multiple(() => Assert.False(sut.IsRunning), () => Assert.False(sut.HasResult));
    }

    [Fact]
    internal async Task StartCommand_WhenTheEngineReportsProgress_ProjectsBytesOntoThePercentAndCaptions()
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
                            new BackupResult(TimeSpan.FromSeconds(3725), 1000, 10, 10)
                        )
                    )
                );
            });

        sut.SourcePath = "source";
        sut.DestinationPath = "destination";

        await sut.StartCommand.ExecuteAsync(null);

        Assert.Multiple(
            () => Assert.True(indeterminateWhileScanning),
            () => Assert.Equal(0d, percentWhileScanning),
            () => Assert.False(sut.IsProgressIndeterminate),
            () => Assert.Equal(25d, sut.ProgressValue, 0.001),
            () =>
                Assert.Equal(
                    string.Format(CultureInfo.CurrentCulture, Strings.ProgressFilesFormat, 4, 10),
                    sut.ProgressText
                ),
            () => Assert.Contains("01:02:05", sut.ElapsedText, StringComparison.Ordinal)
        );
    }

    [Fact]
    internal async Task StartCommand_RunAgain_ClearsThePreviousRunsErrorsAndProgress()
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
                            new BackupResult(TimeSpan.FromSeconds(1), 16, 2, 2)
                        )
                    )
                )
            );

        sut.SourcePath = "source";
        sut.DestinationPath = "destination";

        await sut.StartCommand.ExecuteAsync(null);
        var errorsAfterFirstRun = sut.Errors.Count;

        await sut.StartCommand.ExecuteAsync(null);

        Assert.Multiple(
            () => Assert.Equal(2, errorsAfterFirstRun),
            () => Assert.Empty(sut.Errors),
            () => Assert.Empty(sut.Warnings),
            () => Assert.False(sut.ShowErrors),
            () => Assert.False(sut.ShowWarnings),
            () => Assert.True(sut.ResultIsSuccess),
            () => Assert.Equal(Strings.ResultSuccessTitle, sut.ResultTitle)
        );
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    internal async Task StartCommand_PersistsTheUsedPathsOnlyWhenTheOperationSucceeded(
        bool succeeded
    )
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
                    new BackupResult(TimeSpan.FromSeconds(1), 16, succeeded ? 2 : 1, 2)
                )
            )
        );

        sut.SourcePath = "chosen-source";
        sut.DestinationPath = "chosen-destination";

        await sut.StartCommand.ExecuteAsync(null);

        RecentPathSettings[] expected = succeeded
            ? [new RecentPathSettings("chosen-source", "chosen-destination")]
            : [];

        Assert.Equal<RecentPathSettings>(expected, saved);
    }

    [Fact]
    internal async Task OnNavigatedToAsync_CalledAgain_KeepsWhatTheUserTypedInsteadOfReapplyingRecentPaths()
    {
        var sut = CreateSut();

        await sut.OnNavigatedToAsync();
        var sourceAfterFirstVisit = sut.SourcePath;
        var destinationAfterFirstVisit = sut.DestinationPath;

        sut.DestinationPath = "typed-by-the-user";
        await sut.OnNavigatedToAsync();

        Assert.Multiple(
            () => Assert.Equal("remembered-source", sourceAfterFirstVisit),
            () => Assert.Equal("remembered-destination", destinationAfterFirstVisit),
            () => Assert.Equal("typed-by-the-user", sut.DestinationPath),
            () => _ = Assert.Single(sut.AppliedRecentPaths)
        );
    }

    [Fact]
    internal async Task OnNavigatedToAsync_WhenTheHandlerReportsTheDefaults_LeavesThePageUsable()
    {
        var sut = CreateSut();
        _ = this
            .recentPathsQuery.HandleAsync(
                Arg.Any<GetSettingsQuery<RecentPathSettings>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(RecentPathSettings.DefaultValue);

        await sut.OnNavigatedToAsync();

        Assert.Multiple(
            () => Assert.Empty(sut.SourcePath),
            () => Assert.Empty(sut.DestinationPath),
            () =>
                Assert.Equal<RecentPathSettings>(
                    [RecentPathSettings.DefaultValue],
                    sut.AppliedRecentPaths
                )
        );
    }

    [Theory]
    // cleared once the operation it protected has succeeded
    [InlineData(5, 5, "")]
    // kept after a partial run, which may be re-run
    [InlineData(3, 5, "secret")]
    internal async Task StartCommand_ClearsThePasswordOnlyWhenTheOperationSucceeded(
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

        Assert.Equal(expectedPassword, sut.Password);
    }

    [Fact]
    internal async Task StartCommand_CapturesThePasswordBeforeClearingIt()
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
                            new BackupResult(TimeSpan.FromSeconds(1), 1024, 1, 1)
                        )
                    )
                )
            );

        sut.SourcePath = "source";
        sut.DestinationPath = "destination";
        sut.Password = "secret";

        await sut.StartCommand.ExecuteAsync(null);

        Assert.Equal<string>(["secret"], commands.Select(static command => command.Password));
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
