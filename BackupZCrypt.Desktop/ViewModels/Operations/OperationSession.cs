using System.Collections.ObjectModel;
using System.Globalization;

using BackupZCrypt.Application.Backup;
using BackupZCrypt.Application.Common;
using BackupZCrypt.Desktop.Presentation;
using BackupZCrypt.Desktop.Resources;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BackupZCrypt.Desktop.ViewModels.Operations;

/// <summary>
/// Runs a page's operations and holds what the operation dialog shows about them: the phase, the
/// progress and elapsed time while running, the warnings awaiting confirmation, and the result.
/// </summary>
/// <remarks>
/// The session only knows how to run an <see cref="OperationRun"/> and present its outcome; the page
/// owns its inputs and builds the run from them. A run is kept only while its warnings await
/// confirmation, since continuing past them runs it again; once the session no longer needs it, it
/// drops it, and with it the password the run captured. Each command acts only in the phase whose
/// panel offers it, so a command that arrives late can never interrupt a run.
/// </remarks>
internal sealed partial class OperationSession : ObservableObject
{
    /// <summary>
    /// How often the elapsed time is refreshed while an operation runs, independently of progress
    /// reports, so the clock keeps moving through a long file.
    /// </summary>
    private static readonly TimeSpan ElapsedRefreshInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The cancellation source of the operation currently in flight, or <see langword="null"/> when idle.
    /// </summary>
    private CancellationTokenSource? operationCts;

    /// <summary>
    /// The timestamp the operation currently in flight started at.
    /// </summary>
    private long startTimestamp;

    /// <summary>
    /// The longest elapsed time an engine report has carried during the current run.
    /// </summary>
    private TimeSpan reportedElapsed;

    /// <summary>
    /// The run whose warnings await confirmation, or <see langword="null"/> when none does.
    /// </summary>
    private OperationRun? pendingRun;

    /// <summary>
    /// Occurs when an operation completes and every item succeeded.
    /// </summary>
    public event EventHandler? Succeeded;

    /// <summary>
    /// Gets where the session stands.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning), nameof(ShowWarnings), nameof(HasResult))]
    [NotifyCanExecuteChangedFor(nameof(ContinueAnywayCommand), nameof(DismissWarningsCommand), nameof(DismissResultCommand))]
    public partial OperationPhase Phase { get; private set; }

    /// <summary>
    /// Gets a value indicating whether an operation is running.
    /// </summary>
    public bool IsRunning => Phase is OperationPhase.Running;

    /// <summary>
    /// Gets a value indicating whether warnings await the user's confirmation.
    /// </summary>
    public bool ShowWarnings => Phase is OperationPhase.AwaitingConfirmation;

    /// <summary>
    /// Gets a value indicating whether a result is shown.
    /// </summary>
    public bool HasResult => Phase is OperationPhase.Completed;

    /// <summary>
    /// Gets the completion percentage (0–100) of the running operation.
    /// </summary>
    [ObservableProperty]
    public partial double ProgressValue { get; private set; }

    /// <summary>
    /// Gets a value indicating whether progress is indeterminate because the total size of the
    /// operation is not known yet (the initial scan phase).
    /// </summary>
    [ObservableProperty]
    public partial bool IsProgressIndeterminate { get; private set; }

    /// <summary>
    /// Gets the progress text, such as the number of files processed so far.
    /// </summary>
    [ObservableProperty]
    public partial string ProgressText { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the formatted elapsed time of the running operation.
    /// </summary>
    [ObservableProperty]
    public partial string ElapsedText { get; private set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the completed operation succeeded.
    /// </summary>
    [ObservableProperty]
    public partial bool ResultIsSuccess { get; private set; }

    /// <summary>
    /// Gets the title shown in the result panel.
    /// </summary>
    [ObservableProperty]
    public partial string ResultTitle { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the processed/total files summary shown in the result panel.
    /// </summary>
    [ObservableProperty]
    public partial string ResultFiles { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the duration summary shown in the result panel.
    /// </summary>
    [ObservableProperty]
    public partial string ResultDuration { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the total-size summary shown in the result panel.
    /// </summary>
    [ObservableProperty]
    public partial string ResultSize { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the summary of the files an update kept and removed, shown in the result panel.
    /// </summary>
    [ObservableProperty]
    public partial string ResultUpdateDetails { get; private set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the update summary row is shown.
    /// </summary>
    [ObservableProperty]
    public partial bool HasResultUpdateDetails { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the result detail rows are shown.
    /// </summary>
    [ObservableProperty]
    public partial bool HasResultDetails { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the completed operation's warnings are listed in the result panel.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowResultWarnings { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the error list is shown.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowErrors { get; private set; }

    /// <summary>
    /// Gets the localized warnings the completed operation reported, such as links it did not follow.
    /// </summary>
    public ObservableCollection<string> ResultWarnings { get; } = [];

    /// <summary>
    /// Gets the localized error messages produced by the last operation.
    /// </summary>
    public ObservableCollection<string> Errors { get; } = [];

    /// <summary>
    /// Gets the localized warnings awaiting the user's confirmation.
    /// </summary>
    public ObservableCollection<string> Warnings { get; } = [];

    /// <summary>
    /// Runs an operation, stopping at the confirmation panel when its request raises warnings.
    /// </summary>
    /// <param name="run">The operation to run, built by the page from its current inputs.</param>
    /// <returns>A task that completes once the outcome is shown.</returns>
    /// <exception cref="InvalidOperationException">An operation is already running.</exception>
    public Task RunAsync(OperationRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        if (IsRunning)
        {
            throw new InvalidOperationException("An operation is already running.");
        }

        return ExecuteAsync(run, proceedOnWarnings: false);
    }

    /// <summary>
    /// Formats a duration as hours, minutes, and seconds, counting hours past a day instead of
    /// wrapping them.
    /// </summary>
    /// <param name="duration">The duration to format.</param>
    /// <returns>The duration as <c>hh:mm:ss</c>, with as many hour digits as needed.</returns>
    internal static string FormatDuration(TimeSpan duration)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{(long)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}"
        );
    }

    /// <summary>
    /// Requests cancellation of the operation currently in flight.
    /// </summary>
    [RelayCommand]
    private void CancelOperation()
    {
        operationCts?.Cancel();
    }

    /// <summary>
    /// Runs the operation whose warnings await confirmation again, telling it to proceed past them.
    /// </summary>
    /// <returns>A task that completes once the outcome is shown.</returns>
    [RelayCommand(CanExecute = nameof(CanAnswerWarnings))]
    private Task ContinueAnywayAsync()
    {
        return CanAnswerWarnings() && pendingRun is { } run
            ? ExecuteAsync(run, proceedOnWarnings: true)
            : Task.CompletedTask;
    }

    /// <summary>
    /// Closes the warnings panel and drops the operation without running it.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAnswerWarnings))]
    private void DismissWarnings()
    {
        if (!CanAnswerWarnings())
        {
            return;
        }

        pendingRun = null;
        Warnings.Clear();
        Phase = OperationPhase.Idle;
    }

    /// <summary>
    /// Closes the result panel.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasResult))]
    private void DismissResult()
    {
        if (HasResult)
        {
            Phase = OperationPhase.Idle;
        }
    }

    /// <summary>
    /// Determines whether warnings await an answer, which is the only time continuing or dismissing them
    /// means anything.
    /// </summary>
    /// <returns><see langword="true"/> while the session awaits confirmation of a run's warnings.</returns>
    private bool CanAnswerWarnings()
    {
        return ShowWarnings && pendingRun is not null;
    }

    /// <summary>
    /// Runs the operation off the UI thread, streaming progress to the session and turning the
    /// outcome, a cancellation, or an unexpected exception into what the dialog shows.
    /// </summary>
    /// <remarks>
    /// The operation funnels its own failures into a <c>Result</c>, so the exception handler is
    /// reached only when something escaped it. The raw exception text is wrapped in the localized
    /// unexpected-error frame rather than shown bare, so the sentence the user reads follows their
    /// language even when the detail inside it cannot be translated. The progress sink is constructed
    /// here, on the UI thread, so its callbacks marshal back to it; it coalesces the per-file reports the
    /// engine sends from its worker threads, so the dialog is not flooded and never sees progress run
    /// backwards.
    /// </remarks>
    /// <param name="run">The operation to run.</param>
    /// <param name="proceedOnWarnings">Whether the request should continue past advisory warnings.</param>
    /// <returns>A task that completes once the outcome is shown.</returns>
    private async Task ExecuteAsync(OperationRun run, bool proceedOnWarnings)
    {
        pendingRun = null;
        ResetState();
        IsProgressIndeterminate = true;
        Phase = OperationPhase.Running;

        using CancellationTokenSource cts = new();
        using CancellationTokenSource elapsedCts = new();
        operationCts = cts;
        startTimestamp = TimeProvider.System.GetTimestamp();
        UpdateElapsedText();
        var elapsedTask = RefreshElapsedAsync(elapsedCts.Token);

        try
        {
            CoalescingStatusProgress progress = new(ReportProgress);

            var result = await Task.Run(
                () => run.Execute(proceedOnWarnings, progress, cts.Token),
                cts.Token
            );

            HandleResult(run, result);
        }
        catch (OperationCanceledException)
        {
            ResultIsSuccess = false;
            ResultTitle = Strings.ResultCancelled;
            Phase = OperationPhase.Completed;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ResultIsSuccess = false;
            ResultTitle = Strings.ResultErrorTitle;
            Errors.Add(
                MessageLocalizer.Localize(
                    new LocalizableMessage(MessageCode.UnexpectedErrorFormat, ex.Message)
                )
            );
            ShowErrors = true;
            Phase = OperationPhase.Completed;
        }
        finally
        {
            await elapsedCts.CancelAsync();
            await elapsedTask;

            if (ReferenceEquals(operationCts, cts))
            {
                operationCts = null;

                if (Phase is OperationPhase.Running)
                {
                    Phase = OperationPhase.Idle;
                }
            }
        }
    }

    /// <summary>
    /// Refreshes the elapsed time once a second until the operation ends, so the clock moves even while
    /// no progress report arrives.
    /// </summary>
    /// <param name="cancellationToken">A token cancelled when the operation ends.</param>
    /// <returns>A task that completes once the operation has ended.</returns>
    private async Task RefreshElapsedAsync(CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new(ElapsedRefreshInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                UpdateElapsedText();
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
    }

    /// <summary>
    /// Shows the longer of the time measured here and the time the engine last reported.
    /// </summary>
    private void UpdateElapsedText()
    {
        var measured = TimeProvider.System.GetElapsedTime(startTimestamp);
        var elapsed = measured > reportedElapsed ? measured : reportedElapsed;

        ElapsedText = string.Format(
            CultureInfo.CurrentCulture,
            Strings.ElapsedFormat,
            FormatDuration(elapsed)
        );
    }

    /// <summary>
    /// Projects an engine status report onto the progress bar and its captions, staying indeterminate
    /// while the total size is still unknown.
    /// </summary>
    /// <param name="status">The latest status reported by the engine.</param>
    private void ReportProgress(BackupStatus status)
    {
        IsProgressIndeterminate = status.TotalBytes == 0;
        ProgressValue =
            status.TotalBytes > 0 ? (double)status.ProcessedBytes / status.TotalBytes * 100 : 0;

        ProgressText = string.Format(
            CultureInfo.CurrentCulture,
            Strings.ProgressFilesFormat,
            status.ProcessedFiles,
            status.TotalFiles
        );

        if (status.Elapsed > reportedElapsed)
        {
            reportedElapsed = status.Elapsed;
        }

        UpdateElapsedText();
    }

    /// <summary>
    /// Turns the operation outcome into either a failure list, the warnings confirmation prompt, or
    /// the success/partial summary, and announces a success.
    /// </summary>
    /// <param name="run">The operation that produced the outcome.</param>
    /// <param name="result">The outcome the operation returned.</param>
    private void HandleResult(OperationRun run, Result<BackupOutcome> result)
    {
        if (!result.IsSuccess)
        {
            ShowFailure(run, result.Errors);
            return;
        }

        if (result.Value.Completion is not { } operation)
        {
            foreach (var warning in result.Value.PendingWarnings)
            {
                Warnings.Add(MessageLocalizer.Localize(warning));
            }

            pendingRun = run;
            Phase = OperationPhase.AwaitingConfirmation;
            return;
        }

        ResultIsSuccess = operation.IsSuccess;
        ResultTitle = operation.IsSuccess ? run.SuccessTitle : run.PartialTitle;

        var update = operation.Update;

        ResultFiles =
            update is { RemovedFiles: 0 } && operation.TotalFiles is 0
                ? Strings.ResultNoChanges
                : string.Format(
                    CultureInfo.CurrentCulture,
                    Strings.ResultFilesFormat,
                    operation.ProcessedFiles,
                    operation.TotalFiles
                );

        HasResultUpdateDetails = update is not null;
        ResultUpdateDetails = update is null
            ? string.Empty
            : string.Format(
                CultureInfo.CurrentCulture,
                Strings.ResultUpdateDetailsFormat,
                update.UnchangedFiles,
                update.RemovedFiles
            );

        ResultDuration = string.Format(
            CultureInfo.CurrentCulture,
            Strings.ResultDurationFormat,
            FormatDuration(operation.ElapsedTime)
        );

        ResultSize = string.Format(
            CultureInfo.CurrentCulture,
            Strings.ResultSizeFormat,
            ByteSizeFormatter.Format(operation.TotalBytes)
        );

        HasResultDetails = true;

        foreach (var error in operation.Errors)
        {
            Errors.Add(MessageLocalizer.Localize(error));
        }

        foreach (var warning in operation.Warnings)
        {
            ResultWarnings.Add(MessageLocalizer.Localize(warning));
        }

        ShowErrors = Errors.Count > 0;
        ShowResultWarnings = ResultWarnings.Count > 0;
        Phase = OperationPhase.Completed;

        if (operation.IsSuccess)
        {
            Succeeded?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Shows the result panel in its failed state and lists the localized errors.
    /// </summary>
    /// <param name="run">The operation that failed.</param>
    /// <param name="errors">The messages to localize and display.</param>
    private void ShowFailure(OperationRun run, IEnumerable<LocalizableMessage> errors)
    {
        ResultIsSuccess = false;
        ResultTitle = run.FailureTitle;

        foreach (var error in errors)
        {
            Errors.Add(MessageLocalizer.Localize(error));
        }

        ShowErrors = Errors.Count > 0;
        Phase = OperationPhase.Completed;
    }

    /// <summary>
    /// Clears the progress, result, and message state so a new run starts from an empty panel.
    /// </summary>
    private void ResetState()
    {
        Errors.Clear();
        Warnings.Clear();
        ResultWarnings.Clear();
        ShowErrors = false;
        ShowResultWarnings = false;
        HasResultDetails = false;
        HasResultUpdateDetails = false;
        ResultIsSuccess = false;
        ResultTitle = string.Empty;
        ResultFiles = string.Empty;
        ResultUpdateDetails = string.Empty;
        ResultDuration = string.Empty;
        ResultSize = string.Empty;
        ProgressValue = 0;
        IsProgressIndeterminate = false;
        ProgressText = string.Empty;
        ElapsedText = string.Empty;
        reportedElapsed = TimeSpan.Zero;
    }
}
