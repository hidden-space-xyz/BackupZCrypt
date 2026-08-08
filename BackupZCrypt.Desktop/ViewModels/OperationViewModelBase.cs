using System.Collections.ObjectModel;
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
using BackupZCrypt.Domain.ValueObjects.Backup;
using BackupZCrypt.Domain.ValueObjects.Localization;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BackupZCrypt.Desktop.ViewModels;

/// <summary>
/// Shared engine for the create/update/restore/verify pages: message execution with progress reporting,
/// cancellation, the warnings confirmation flow, and the final result panel. Subclasses only describe
/// how to build their message and which handler executes it.
/// </summary>
/// <param name="recentPathsQuery">The handler that loads the recently used paths.</param>
/// <param name="saveRecentPathsCommand">The handler that persists the recently used paths.</param>
/// <param name="filePicker">The folder picker service.</param>
internal abstract partial class OperationViewModelBase(
    IQueryHandler<GetSettingsQuery<RecentPathSettings>, RecentPathSettings> recentPathsQuery,
    ICommandHandler<SaveSettingsCommand<RecentPathSettings>, Result> saveRecentPathsCommand,
    IFilePickerService filePicker
) : ViewModelBase
{
    /// <summary>
    /// The cancellation source of the operation currently in flight, or <see langword="null"/> when idle.
    /// </summary>
    private CancellationTokenSource? operationCts;

    /// <summary>
    /// A value indicating whether the remembered paths have already been applied, so returning to the
    /// page never overwrites what the user has typed since.
    /// </summary>
    private bool recentPathsLoaded;

    /// <summary>
    /// Gets or sets the source path of the operation.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial string SourcePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the destination path of the operation.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial string DestinationPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the password protecting the backup.
    /// </summary>
    /// <remarks>
    /// Declared once here rather than on each page so there is a single owner of the secret, and so
    /// <see cref="HandleResult"/> has one place to clear it from once the operation has succeeded.
    /// </remarks>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial string Password { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether an operation is currently running.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial bool IsRunning { get; set; }

    /// <summary>
    /// Gets or sets the completion percentage (0–100) of the running operation.
    /// </summary>
    [ObservableProperty]
    public partial double ProgressValue { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether progress is indeterminate because the total size of
    /// the operation is not known yet (the initial scan phase).
    /// </summary>
    [ObservableProperty]
    public partial bool IsProgressIndeterminate { get; set; }

    /// <summary>
    /// Gets or sets the progress text, such as the number of files processed so far.
    /// </summary>
    [ObservableProperty]
    public partial string ProgressText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the formatted elapsed time of the running operation.
    /// </summary>
    [ObservableProperty]
    public partial string ElapsedText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the result panel is shown.
    /// </summary>
    [ObservableProperty]
    public partial bool HasResult { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the completed operation succeeded.
    /// </summary>
    [ObservableProperty]
    public partial bool ResultIsSuccess { get; set; }

    /// <summary>
    /// Gets or sets the title shown in the result panel.
    /// </summary>
    [ObservableProperty]
    public partial string ResultTitle { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the processed/total files summary shown in the result panel.
    /// </summary>
    [ObservableProperty]
    public partial string ResultFiles { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the duration summary shown in the result panel.
    /// </summary>
    [ObservableProperty]
    public partial string ResultDuration { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the total-size summary shown in the result panel.
    /// </summary>
    [ObservableProperty]
    public partial string ResultSize { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the result detail rows are shown.
    /// </summary>
    [ObservableProperty]
    public partial bool HasResultDetails { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the warnings confirmation panel is shown.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowWarnings { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the error list is shown.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowErrors { get; set; }

    /// <summary>
    /// Gets the localized error messages produced by the last operation.
    /// </summary>
    public ObservableCollection<string> Errors { get; } = [];

    /// <summary>
    /// Gets the localized warning messages produced by the last operation.
    /// </summary>
    public ObservableCollection<string> Warnings { get; } = [];

    /// <summary>
    /// Gets the folder picker service.
    /// </summary>
    protected IFilePickerService FilePicker { get; } = filePicker;

    /// <summary>
    /// Loads and applies the most recently used paths the first time the page is shown.
    /// </summary>
    /// <remarks>
    /// Recent paths are a convenience only, and the handler absorbs a failure to read them into the
    /// defaults, so the page simply starts with empty inputs in that case.
    /// </remarks>
    /// <returns>A task that completes once the recent paths have been applied.</returns>
    public override async Task OnNavigatedToAsync()
    {
        if (recentPathsLoaded)
        {
            return;
        }

        recentPathsLoaded = true;

        var recent = await recentPathsQuery.HandleAsync(
            new GetSettingsQuery<RecentPathSettings>(),
            CancellationToken.None
        );

        ApplyRecentPaths(recent);
    }

    /// <summary>
    /// Builds the page's message from the current inputs and dispatches it to the page's handler.
    /// </summary>
    /// <param name="proceedOnWarnings">Whether the operation should continue past warnings.</param>
    /// <param name="progress">The sink that receives incremental status updates.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The outcome of the operation.</returns>
    protected abstract Task<Result<BackupOutcome>> ExecuteOperationAsync(
        bool proceedOnWarnings,
        IProgress<BackupStatus> progress,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Applies the recently used paths to the page inputs. The default implementation does nothing.
    /// </summary>
    /// <param name="recent">The recently used paths.</param>
    protected virtual void ApplyRecentPaths(RecentPathSettings recent)
    {
    }

    /// <summary>
    /// Called after <see cref="Password"/> changes, so a page can refresh whatever it derives from it.
    /// The default implementation does nothing.
    /// </summary>
    /// <param name="value">The new password.</param>
    protected virtual void OnPasswordUpdated(string value)
    {
    }

    /// <summary>
    /// Forwards the generated change notification to the overridable hook.
    /// </summary>
    /// <param name="value">The new password.</param>
    partial void OnPasswordChanged(string value)
    {
        OnPasswordUpdated(value);
    }

    /// <summary>
    /// Gets a value indicating whether a destination path is required in addition to a source. Pages
    /// without a destination (such as verification) override this to return <see langword="false"/>.
    /// </summary>
    protected virtual bool RequiresDestination => true;

    /// <summary>
    /// Determines whether the operation can start, requiring that no operation is running, the source
    /// path is set, and the destination path is set when <see cref="RequiresDestination"/> is <see langword="true"/>.
    /// </summary>
    /// <returns><see langword="true"/> if the operation may begin; otherwise <see langword="false"/>.</returns>
    protected virtual bool CanStart()
    {
        return !IsRunning
            && !string.IsNullOrWhiteSpace(SourcePath)
            && (!RequiresDestination || !string.IsNullOrWhiteSpace(DestinationPath));
    }

    /// <summary>
    /// Prompts the user to pick a folder and, when one is chosen, passes it to <paramref name="assign"/>.
    /// </summary>
    /// <param name="assign">Receives the selected folder path when the user picks one.</param>
    /// <returns>A task that completes once the folder picker has been dismissed.</returns>
    protected async Task PickFolderIntoAsync(Action<string> assign)
    {
        ArgumentNullException.ThrowIfNull(assign);

        var path = await FilePicker.PickFolderAsync(Strings.PickFolderTitle);
        if (path is not null)
        {
            assign(path);
        }
    }

    /// <summary>
    /// Re-evaluates whether the start command can execute.
    /// </summary>
    protected void NotifyStartCanExecuteChanged()
    {
        StartCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Invoked when the source or destination path changes. The default implementation does nothing.
    /// </summary>
    protected virtual void HandlePathChanged()
    {
    }

    /// <summary>
    /// Reacts to a change of the source path by re-running the page's path-dependent work.
    /// </summary>
    /// <param name="value">The new source path.</param>
    partial void OnSourcePathChanged(string value)
    {
        HandlePathChanged();
    }

    /// <summary>
    /// Reacts to a change of the destination path by re-running the page's path-dependent work.
    /// </summary>
    /// <param name="value">The new destination path.</param>
    partial void OnDestinationPathChanged(string value)
    {
        HandlePathChanged();
    }

    /// <summary>
    /// Starts the operation, stopping at the confirmation panel when the request raises warnings.
    /// </summary>
    /// <returns>A task that completes once the operation has finished and its result is shown.</returns>
    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task StartAsync()
    {
        return RunAsync(proceedOnWarnings: false);
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
    /// Re-runs the operation after the user acknowledged the warnings, telling the pipeline to
    /// proceed past them.
    /// </summary>
    /// <returns>A task that completes once the operation has finished and its result is shown.</returns>
    [RelayCommand]
    private Task ContinueAnywayAsync()
    {
        ShowWarnings = false;
        return RunAsync(proceedOnWarnings: true);
    }

    /// <summary>
    /// Closes the warnings panel and discards the listed warnings without starting the operation.
    /// </summary>
    [RelayCommand]
    private void DismissWarnings()
    {
        ShowWarnings = false;
        Warnings.Clear();
    }

    /// <summary>
    /// Closes the result panel.
    /// </summary>
    [RelayCommand]
    private void DismissResult()
    {
        HasResult = false;
    }

    /// <summary>
    /// Runs the operation off the UI thread, streaming progress to the page and turning the outcome,
    /// a cancellation, or an unexpected exception into the result panel.
    /// </summary>
    /// <remarks>
    /// The handler funnels its own failures into a <c>Result</c>, so the exception handler is
    /// reached only when something escaped it. The raw exception text is wrapped in the localized
    /// unexpected-error frame rather than shown bare, so the sentence the user reads follows their
    /// language even when the detail inside it cannot be translated. The progress sink is constructed
    /// here, on the UI thread, so its callbacks marshal back to it.
    /// </remarks>
    /// <param name="proceedOnWarnings">Whether the request should continue past advisory warnings.</param>
    /// <returns>A task that completes once the result has been presented.</returns>
    private async Task RunAsync(bool proceedOnWarnings)
    {
        ResetState();
        IsProgressIndeterminate = true;
        IsRunning = true;

        using CancellationTokenSource cts = new();
        operationCts = cts;

        try
        {
            Progress<BackupStatus> progress = new(ReportProgress);

            var result = await Task.Run(
                () => ExecuteOperationAsync(proceedOnWarnings, progress, cts.Token),
                cts.Token
            );

            HandleResult(result);
        }
        catch (OperationCanceledException)
        {
            HasResult = true;
            ResultIsSuccess = false;
            ResultTitle = Strings.ResultCancelled;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            HasResult = true;
            ResultIsSuccess = false;
            ResultTitle = Strings.ResultErrorTitle;

            Errors.Add(
                MessageLocalizer.Localize(
                    new LocalizableMessage(MessageCode.UnexpectedErrorFormat, ex.Message)
                )
            );
            ShowErrors = true;
        }
        finally
        {
            IsRunning = false;
            operationCts = null;
        }
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

        ElapsedText = string.Format(
            CultureInfo.CurrentCulture,
            Strings.ElapsedFormat,
            status.Elapsed.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)
        );
    }

    /// <summary>
    /// Gets the result title shown when the operation completes successfully. Overridable so each
    /// page can phrase success in its own terms (for example, "Backup is intact" for verification).
    /// </summary>
    protected virtual string SuccessResultTitle => Strings.ResultSuccessTitle;

    /// <summary>
    /// Gets the result title shown when the operation finishes but some items failed.
    /// </summary>
    protected virtual string PartialResultTitle => Strings.ResultPartialTitle;

    /// <summary>
    /// Gets the result title shown when the operation could not be performed.
    /// </summary>
    protected virtual string FailureResultTitle => Strings.ResultErrorTitle;

    /// <summary>
    /// Turns the handler outcome into either a failure list, the warnings confirmation prompt, or
    /// the success/partial summary, and remembers the used paths when the operation succeeded.
    /// </summary>
    /// <param name="result">The outcome returned by the page's handler.</param>
    private void HandleResult(Result<BackupOutcome> result)
    {
        if (!result.IsSuccess)
        {
            ShowFailure(result.Errors);
            return;
        }

        if (result.Value.Completion is not { } operation)
        {
            foreach (var warning in result.Value.PendingWarnings)
            {
                Warnings.Add(MessageLocalizer.Localize(warning));
            }

            ShowWarnings = true;
            return;
        }

        HasResult = true;
        ResultIsSuccess = operation.IsSuccess;
        ResultTitle = operation.IsSuccess ? SuccessResultTitle : PartialResultTitle;

        ResultFiles = string.Format(
            CultureInfo.CurrentCulture,
            Strings.ResultFilesFormat,
            operation.ProcessedFiles,
            operation.TotalFiles
        );

        ResultDuration = string.Format(
            CultureInfo.CurrentCulture,
            Strings.ResultDurationFormat,
            operation.ElapsedTime.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)
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

        ShowErrors = Errors.Count > 0;

        if (operation.IsSuccess)
        {
            _ = TrySaveRecentPathsAsync();
            ClearPassword();
        }
    }

    /// <summary>
    /// Drops the password once the operation it protected has succeeded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every page ViewModel is a singleton held for the lifetime of the window, so without this the
    /// plaintext password stays reachable from a live object for as long as the application runs.
    /// Be clear about what this does and does not buy: a .NET <see cref="string"/> cannot be zeroed,
    /// and the bound <c>TextBox</c> keeps its own copy, so this only drops the last permanently-held
    /// reference and lets the garbage collector reclaim it eventually. It is not a wipe.
    /// </para>
    /// <para>
    /// Only on success, and only after <see cref="ExecuteOperationAsync"/> has already captured the
    /// value into its message. Clearing on failure would be actively hostile: the commonest reason to
    /// re-run a restore is a mistyped password, and the user would have to retype a fifty-character
    /// generated one.
    /// </para>
    /// </remarks>
    protected virtual void ClearPassword()
    {
        Password = string.Empty;
    }

    /// <summary>
    /// Shows the result panel in its failed state and lists the localized errors.
    /// </summary>
    /// <param name="errors">The messages to localize and display.</param>
    private void ShowFailure(IEnumerable<LocalizableMessage> errors)
    {
        HasResult = true;
        ResultIsSuccess = false;
        ResultTitle = FailureResultTitle;

        foreach (var error in errors)
        {
            Errors.Add(MessageLocalizer.Localize(error));
        }

        ShowErrors = Errors.Count > 0;
    }

    /// <summary>
    /// Clears the progress, result, and message state so a new run starts from an empty panel.
    /// </summary>
    private void ResetState()
    {
        Errors.Clear();
        Warnings.Clear();
        ShowErrors = false;
        ShowWarnings = false;
        HasResult = false;
        HasResultDetails = false;
        ResultIsSuccess = false;
        ResultTitle = string.Empty;
        ResultFiles = string.Empty;
        ResultDuration = string.Empty;
        ResultSize = string.Empty;
        ProgressValue = 0;
        IsProgressIndeterminate = false;
        ProgressText = string.Empty;
        ElapsedText = string.Empty;
    }

    /// <summary>
    /// Builds the recent-path settings to persist after a successful operation. The default records
    /// both the source and destination; pages without a destination (such as verification) can
    /// override this to avoid clearing a remembered path.
    /// </summary>
    /// <param name="current">The currently persisted recent paths.</param>
    /// <returns>The recent paths to save.</returns>
    protected virtual RecentPathSettings BuildRecentPaths(RecentPathSettings current)
    {
        ArgumentNullException.ThrowIfNull(current);

        return current with
        {
            LastSourcePath = SourcePath,
            LastDestinationPath = DestinationPath,
        };
    }

    /// <summary>
    /// Persists the paths of the completed operation so the pages can pre-fill them next time.
    /// </summary>
    /// <remarks>
    /// Remembering the last-used paths is a best-effort convenience, and both handlers absorb their
    /// own storage failures: it must not affect the already-completed operation. The write is
    /// deliberately not cancellable — the operation has finished, and cancelling it must not discard
    /// where it ran.
    /// </remarks>
    /// <returns><see langword="true"/> if the paths were persisted; otherwise <see langword="false"/>.</returns>
    private async Task<bool> TrySaveRecentPathsAsync()
    {
        var recent = await recentPathsQuery.HandleAsync(
            new GetSettingsQuery<RecentPathSettings>(),
            CancellationToken.None
        );

        var saved = await saveRecentPathsCommand.HandleAsync(
            new SaveSettingsCommand<RecentPathSettings>(BuildRecentPaths(recent)),
            CancellationToken.None
        );

        return saved.IsSuccess;
    }
}
