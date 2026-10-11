using System.Globalization;

using BackupZCrypt.Application.Backup;
using BackupZCrypt.Application.Common;
using BackupZCrypt.Desktop.Resources;
using BackupZCrypt.Desktop.ViewModels.Operations;
using BackupZCrypt.Test.Common;

namespace BackupZCrypt.Test.Unit.Desktop.ViewModels.Operations;

/// <summary>
/// Unit tests for <see cref="OperationSession"/>, which runs a page's operation and holds what the
/// operation dialog shows: one phase at a time, and the run only for as long as it may be continued.
/// </summary>
public sealed class OperationSessionTests : IDisposable
{
    /// <summary>
    /// The synchronization context that was installed before the test, restored afterwards.
    /// </summary>
    private readonly SynchronizationContext? previousContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="OperationSessionTests"/> class, installing a
    /// synchronization context that runs posted callbacks inline so progress reports arrive
    /// synchronously.
    /// </summary>
    public OperationSessionTests()
    {
        previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new InlineSynchronizationContext());
    }

    /// <summary>
    /// Restores the synchronization context so the inline one cannot leak into other fixtures.
    /// </summary>
    public void Dispose()
    {
        SynchronizationContext.SetSynchronizationContext(previousContext);
    }

    [Fact]
    internal void NewSession_IsIdleAndShowsNothing()
    {
        OperationSession sut = new();

        Assert.Multiple(
            () => Assert.Equal(OperationPhase.Idle, sut.Phase),
            () => Assert.False(sut.IsRunning),
            () => Assert.False(sut.ShowWarnings),
            () => Assert.False(sut.HasResult)
        );
    }

    [Fact]
    internal async Task RunAsync_WhileTheOperationRuns_IsInTheRunningPhase()
    {
        OperationSession sut = new();
        TaskCompletionSource<Result<BackupOutcome>> release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        var running = sut.RunAsync(Run((_, _, _) => release.Task));
        var phaseWhileRunning = sut.Phase;
        release.SetResult(Completed(isSuccess: true));
        await running;

        Assert.Multiple(
            () => Assert.Equal(OperationPhase.Running, phaseWhileRunning),
            () => Assert.Equal(OperationPhase.Completed, sut.Phase)
        );
    }

    [Fact]
    internal async Task RunAsync_FullSuccess_ShowsTheSuccessTitleAndAnnouncesIt()
    {
        OperationSession sut = new();
        var succeeded = 0;
        sut.Succeeded += (_, _) => succeeded++;

        await sut.RunAsync(Run((_, _, _) => Task.FromResult(Completed(isSuccess: true))));

        Assert.Multiple(
            () => Assert.Equal(OperationPhase.Completed, sut.Phase),
            () => Assert.True(sut.ResultIsSuccess),
            () => Assert.Equal("success", sut.ResultTitle),
            () => Assert.Equal(1, succeeded)
        );
    }

    [Fact]
    internal async Task RunAsync_PartialSuccess_ShowsThePartialTitleWithoutAnnouncingSuccess()
    {
        OperationSession sut = new();
        var succeeded = 0;
        sut.Succeeded += (_, _) => succeeded++;

        await sut.RunAsync(Run((_, _, _) => Task.FromResult(Completed(isSuccess: false))));

        Assert.Multiple(
            () => Assert.False(sut.ResultIsSuccess),
            () => Assert.Equal("partial", sut.ResultTitle),
            () => Assert.Equal(0, succeeded)
        );
    }

    [Fact]
    internal async Task RunAsync_Failure_ShowsTheFailureTitleAndTheErrors()
    {
        OperationSession sut = new();

        await sut.RunAsync(
            Run((_, _, _) => Task.FromResult(Result<BackupOutcome>.Failure(MessageCode.InvalidPassword)))
        );

        Assert.Multiple(
            () => Assert.Equal(OperationPhase.Completed, sut.Phase),
            () => Assert.Equal("failure", sut.ResultTitle),
            () => Assert.Equal([Strings.GetByKey(nameof(MessageCode.InvalidPassword))], sut.Errors),
            () => Assert.True(sut.ShowErrors)
        );
    }

    [Fact]
    internal async Task ContinueAnyway_RunsTheSameOperationAgainPastItsWarnings()
    {
        OperationSession sut = new();
        List<bool> proceedFlags = [];

        await sut.RunAsync(
            Run(
                (proceed, _, _) =>
                {
                    proceedFlags.Add(proceed);
                    return Task.FromResult(proceed ? Completed(isSuccess: true) : Warned());
                }
            )
        );
        var phaseBeforeContinuing = sut.Phase;
        await sut.ContinueAnywayCommand.ExecuteAsync(null);

        Assert.Multiple(
            () => Assert.Equal(OperationPhase.AwaitingConfirmation, phaseBeforeContinuing),
            () => Assert.Equal([false, true], proceedFlags),
            () => Assert.Equal(OperationPhase.Completed, sut.Phase)
        );
    }

    [Fact]
    internal async Task ContinueAnyway_AfterTheWarningsWereDismissed_RunsNothing()
    {
        OperationSession sut = new();
        var runs = 0;

        await sut.RunAsync(
            Run(
                (_, _, _) =>
                {
                    runs++;
                    return Task.FromResult(Warned());
                }
            )
        );
        sut.DismissWarningsCommand.Execute(null);
        await sut.ContinueAnywayCommand.ExecuteAsync(null);

        Assert.Multiple(
            () => Assert.Equal(1, runs),
            () => Assert.Equal(OperationPhase.Idle, sut.Phase),
            () => Assert.Empty(sut.Warnings)
        );
    }

    [Fact]
    internal async Task ContinueAnyway_AfterTheOperationCompleted_RunsNothingSinceTheRunWasReleased()
    {
        OperationSession sut = new();
        var runs = 0;

        await sut.RunAsync(
            Run(
                (_, _, _) =>
                {
                    runs++;
                    return Task.FromResult(Completed(isSuccess: true));
                }
            )
        );
        await sut.ContinueAnywayCommand.ExecuteAsync(null);

        Assert.Equal(1, runs);
    }

    [Fact]
    internal async Task DismissResult_ReturnsToIdle()
    {
        OperationSession sut = new();
        await sut.RunAsync(Run((_, _, _) => Task.FromResult(Completed(isSuccess: true))));

        sut.DismissResultCommand.Execute(null);

        Assert.Equal(OperationPhase.Idle, sut.Phase);
    }

    [Fact]
    internal async Task RunAsync_ProgressReportedLongerThanMeasured_ShowsTheReportedElapsedTime()
    {
        OperationSession sut = new();
        TaskCompletionSource<Result<BackupOutcome>> release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        TaskCompletionSource reported = new(TaskCreationOptions.RunContinuationsAsynchronously);

        var running = sut.RunAsync(
            Run(
                (_, progress, _) =>
                {
                    progress.Report(new BackupStatus(1, 2, 50, 100, TimeSpan.FromHours(30)));
                    reported.SetResult();
                    return release.Task;
                }
            )
        );
        await reported.Task;
        var elapsedWhileRunning = sut.ElapsedText;
        var indeterminateWhileRunning = sut.IsProgressIndeterminate;
        release.SetResult(Completed(isSuccess: true));
        await running;

        Assert.Multiple(
            () => Assert.Contains("30:00:00", elapsedWhileRunning, StringComparison.Ordinal),
            () => Assert.False(indeterminateWhileRunning)
        );
    }

    [Fact]
    internal async Task Phase_FromStartThroughContinueToDismiss_NeverPassesThroughIdleOnContinue()
    {
        OperationSession sut = new();
        List<OperationPhase> phases = [];
        sut.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(OperationSession.Phase))
            {
                phases.Add(sut.Phase);
            }
        };

        await sut.RunAsync(
            Run((proceed, _, _) => Task.FromResult(proceed ? Completed(isSuccess: true) : Warned()))
        );
        await sut.ContinueAnywayCommand.ExecuteAsync(null);
        sut.DismissResultCommand.Execute(null);

        Assert.Equal(
            [
                OperationPhase.Running,
                OperationPhase.AwaitingConfirmation,
                OperationPhase.Running,
                OperationPhase.Completed,
                OperationPhase.Idle,
            ],
            phases
        );
    }

    [Fact]
    internal async Task CommandsForOtherPanels_WhileTheOperationRuns_LeaveItRunning()
    {
        OperationSession sut = new();
        TaskCompletionSource<Result<BackupOutcome>> release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        TaskCompletionSource<CancellationToken> continuedToken = new();
        await sut.RunAsync(WarnThenBlock(release, continuedToken));
        var continuing = sut.ContinueAnywayCommand.ExecuteAsync(null);

        sut.DismissResultCommand.Execute(null);
        sut.DismissWarningsCommand.Execute(null);
        await sut.ContinueAnywayCommand.ExecuteAsync(null);
        var phaseAfterLateCommands = sut.Phase;
        var commandsOffered = (
            Continue: sut.ContinueAnywayCommand.CanExecute(null),
            DismissWarnings: sut.DismissWarningsCommand.CanExecute(null),
            DismissResult: sut.DismissResultCommand.CanExecute(null)
        );
        var startAgain = await Record.ExceptionAsync(
            () => sut.RunAsync(Run((_, _, _) => Task.FromResult(Completed(isSuccess: true))))
        );
        release.SetResult(Completed(isSuccess: true));
        await continuing;

        Assert.Multiple(
            () => Assert.Equal(OperationPhase.Running, phaseAfterLateCommands),
            () => Assert.Equal((false, false, false), commandsOffered),
            () => Assert.IsType<InvalidOperationException>(startAgain),
            () => Assert.Equal(OperationPhase.Completed, sut.Phase)
        );
    }

    [Fact]
    internal async Task ContinueAnyway_StartedBeforeTheFirstRunFinishedCleaningUp_StaysRunningAndCancellable()
    {
        OperationSession sut = new();
        TaskCompletionSource<Result<BackupOutcome>> release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        TaskCompletionSource<CancellationToken> continuedToken = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        Task? continuing = null;
        sut.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(OperationSession.Phase) && sut.ShowWarnings && continuing is null)
            {
                continuing = sut.ContinueAnywayCommand.ExecuteAsync(null);
            }
        };

        await sut.RunAsync(WarnThenBlock(release, continuedToken));
        var phaseAfterFirstRunEnded = sut.Phase;
        var token = await continuedToken.Task;
        sut.CancelOperationCommand.Execute(null);
        var cancelReachedTheContinuedRun = token.IsCancellationRequested;
        release.SetResult(Completed(isSuccess: true));
        await continuing!;

        Assert.Multiple(
            () => Assert.Equal(OperationPhase.Running, phaseAfterFirstRunEnded),
            () => Assert.True(cancelReachedTheContinuedRun)
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    internal async Task ContinueAnyway_AfterAFailureOrCancellation_IsNotOffered(bool cancelled)
    {
        OperationSession sut = new();
        var runs = 0;

        await sut.RunAsync(
            Run(
                (_, _, _) =>
                {
                    runs++;
                    return cancelled
                        ? Task.FromCanceled<Result<BackupOutcome>>(new CancellationToken(canceled: true))
                        : Task.FromResult(Result<BackupOutcome>.Failure(MessageCode.InvalidPassword));
                }
            )
        );
        var offered = sut.ContinueAnywayCommand.CanExecute(null);
        await sut.ContinueAnywayCommand.ExecuteAsync(null);

        Assert.Multiple(
            () => Assert.False(offered),
            () => Assert.Equal(1, runs),
            () => Assert.Equal(OperationPhase.Completed, sut.Phase)
        );
    }

    [Theory]
    [InlineData(0, "00:00:00")]
    [InlineData(3_723, "01:02:03")]
    [InlineData(90_061, "25:01:01")]
    internal void FormatDuration_CountsHoursPastADayInsteadOfWrapping(int seconds, string expected)
    {
        Assert.Equal(expected, OperationSession.FormatDuration(TimeSpan.FromSeconds(seconds)));
    }

    [Theory]
    [InlineData(0, false, 0, 0, false)]
    [InlineData(2, false, 0, 0, false)]
    [InlineData(2, true, 0, 0, false)]
    [InlineData(0, true, 0, 0, true)]
    [InlineData(0, true, 4, 0, true)]
    [InlineData(0, true, 3, 1, false)]
    internal async Task RunAsync_Completed_ShowsTheUpdateRowForEveryUpdateAndNoChangesOnlyWhenNothingChanged(
        int files,
        bool isUpdate,
        int unchangedFiles,
        int removedFiles,
        bool nothingChanged
    )
    {
        OperationSession sut = new();
        UpdateSummary? update = isUpdate ? new(unchangedFiles, removedFiles) : null;
        BackupResult completion = new(TimeSpan.FromSeconds(1), 16, files, files, update: update);

        await sut.RunAsync(
            Run((_, _, _) => Task.FromResult(Result<BackupOutcome>.Success(BackupOutcome.Completed(completion))))
        );

        var expectedFiles = nothingChanged
            ? Strings.ResultNoChanges
            : string.Format(CultureInfo.CurrentCulture, Strings.ResultFilesFormat, files, files);
        var expectedUpdate = isUpdate
            ? string.Format(CultureInfo.CurrentCulture, Strings.ResultUpdateDetailsFormat, unchangedFiles, removedFiles)
            : string.Empty;

        Assert.Multiple(
            () => Assert.Equal(expectedFiles, sut.ResultFiles),
            () => Assert.Equal(isUpdate, sut.HasResultUpdateDetails),
            () => Assert.Equal(expectedUpdate, sut.ResultUpdateDetails)
        );
    }

    [Fact]
    internal async Task RunAsync_WithoutARun_IsRejected()
    {
        OperationSession sut = new();

        _ = await Assert.ThrowsAsync<ArgumentNullException>(() => sut.RunAsync(null!));
    }

    /// <summary>
    /// Builds a run with recognizable titles around the given execution.
    /// </summary>
    /// <param name="execute">The execution the run performs.</param>
    /// <returns>The run.</returns>
    private static OperationRun Run(OperationExecution execute)
    {
        return new OperationRun(execute, "success", "partial", "failure");
    }

    /// <summary>
    /// Builds a completed outcome over one file that either succeeded or failed.
    /// </summary>
    /// <param name="isSuccess">Whether the file succeeded.</param>
    /// <returns>The outcome.</returns>
    private static Result<BackupOutcome> Completed(bool isSuccess)
    {
        return Result<BackupOutcome>.Success(
            BackupOutcome.Completed(new BackupResult(TimeSpan.FromSeconds(1), 16, isSuccess ? 1 : 0, 1))
        );
    }

    /// <summary>
    /// Builds an outcome that stopped to ask the user to confirm a warning.
    /// </summary>
    /// <returns>The outcome.</returns>
    private static Result<BackupOutcome> Warned()
    {
        return Result<BackupOutcome>.Success(
            BackupOutcome.AwaitingConfirmation([new LocalizableMessage(MessageCode.DestinationContainsBackup)])
        );
    }

    /// <summary>
    /// Builds a run that stops at the warnings the first time and blocks until released when it is
    /// continued, recording the token the continued run received.
    /// </summary>
    /// <param name="release">Completes the continued run.</param>
    /// <param name="continuedToken">Receives the token of the continued run.</param>
    /// <returns>The run.</returns>
    private static OperationRun WarnThenBlock(
        TaskCompletionSource<Result<BackupOutcome>> release,
        TaskCompletionSource<CancellationToken> continuedToken
    )
    {
        return Run(
            (proceed, _, token) =>
            {
                if (!proceed)
                {
                    return Task.FromResult(Warned());
                }

                continuedToken.TrySetResult(token);
                return release.Task;
            }
        );
    }
}
