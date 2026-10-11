using System.ComponentModel;

using BackupZCrypt.Application.Settings;
using BackupZCrypt.Desktop.Resources;
using BackupZCrypt.Desktop.ViewModels.Operations;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BackupZCrypt.Desktop.ViewModels;

/// <summary>
/// Shared page for the create/update/restore/verify operations: the source, destination, and password
/// inputs, the remembered paths, and the start command. Running the operation and presenting its
/// outcome belong to <see cref="Session"/>; subclasses only describe how to capture their request and
/// which operation runs it.
/// </summary>
internal abstract partial class OperationViewModelBase : ViewModelBase
{
    /// <summary>
    /// A value indicating whether the remembered paths have already been applied, so returning to the
    /// page never overwrites what the user has typed since.
    /// </summary>
    private bool recentPathsLoaded;

    /// <summary>
    /// Initializes a new instance of the <see cref="OperationViewModelBase"/> class.
    /// </summary>
    /// <param name="settingsStore">The store the page loads and saves its settings through.</param>
    /// <param name="filePicker">The folder picker service.</param>
    protected OperationViewModelBase(ISettingsStore settingsStore, IFilePickerService filePicker)
    {
        SettingsStore = settingsStore;
        FilePicker = filePicker;
        Session = new OperationSession();
        Session.PropertyChanged += OnSessionPropertyChanged;
        Session.Succeeded += OnOperationSucceeded;
    }

    /// <summary>
    /// Gets the session that runs the page's operations and holds what the operation dialog shows.
    /// </summary>
    public OperationSession Session { get; }

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
    /// <see cref="ClearPassword"/> has one place to clear it from once the operation has succeeded.
    /// </remarks>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial string Password { get; set; } = string.Empty;

    /// <summary>
    /// Gets the folder picker service.
    /// </summary>
    protected IFilePickerService FilePicker { get; }

    /// <summary>
    /// Gets the store the page loads and saves its settings through, including the recently used paths.
    /// </summary>
    protected ISettingsStore SettingsStore { get; }

    /// <summary>
    /// Gets a value indicating whether a destination path is required in addition to a source. Pages
    /// without a destination (such as verification) override this to return <see langword="false"/>.
    /// </summary>
    protected virtual bool RequiresDestination => true;

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
    /// Loads and applies the most recently used paths the first time the page is shown.
    /// </summary>
    /// <remarks>
    /// Recent paths are a convenience only, and the settings store absorbs a failure to read them
    /// into the defaults, so the page simply starts with empty inputs in that case.
    /// </remarks>
    /// <returns>A task that completes once the recent paths have been applied.</returns>
    public override async Task OnNavigatedToAsync()
    {
        if (recentPathsLoaded)
        {
            return;
        }

        recentPathsLoaded = true;

        var recent = await SettingsStore.LoadAsync<RecentPathSettings>(CancellationToken.None);

        ApplyRecentPaths(recent);
    }

    /// <summary>
    /// Captures the page's request from the current inputs and returns the operation that runs it.
    /// </summary>
    /// <remarks>
    /// Called on the UI thread when the user starts the operation, so the request reflects exactly
    /// what was on the page then. The same execution runs again if the user continues past warnings.
    /// </remarks>
    /// <returns>The execution of the page's operation with the captured request.</returns>
    protected abstract OperationExecution CaptureExecution();

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
    /// Determines whether the operation can start, requiring that no operation is running, the source
    /// path is set, and the destination path is set when <see cref="RequiresDestination"/> is <see langword="true"/>.
    /// </summary>
    /// <returns><see langword="true"/> if the operation may begin; otherwise <see langword="false"/>.</returns>
    protected virtual bool CanStart()
    {
        return !Session.IsRunning
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
    /// Drops the password once the operation it protected has succeeded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every page ViewModel is a singleton held for the lifetime of the window, so without this the
    /// plaintext password stays reachable from a live object for as long as the application runs.
    /// Be clear about what this does and does not buy: a .NET <see cref="string"/> cannot be zeroed,
    /// and the bound <c>TextBox</c> keeps its own copy, so this only drops the last permanently-held
    /// reference and lets the garbage collector reclaim it eventually. It is not a wipe. The session
    /// drops the execution that captured the password as soon as it no longer needs it.
    /// </para>
    /// <para>
    /// Only on success. Clearing on failure would be actively hostile: the commonest reason to re-run
    /// a restore is a mistyped password, and the user would have to retype a fifty-character
    /// generated one.
    /// </para>
    /// </remarks>
    protected virtual void ClearPassword()
    {
        Password = string.Empty;
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
    /// Forwards the generated change notification to the overridable hook.
    /// </summary>
    /// <param name="value">The new password.</param>
    partial void OnPasswordChanged(string value)
    {
        OnPasswordUpdated(value);
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
    /// Starts the operation with the request captured from the current inputs.
    /// </summary>
    /// <returns>A task that completes once the outcome is shown.</returns>
    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task StartAsync()
    {
        return Session.RunAsync(
            new OperationRun(CaptureExecution(), SuccessResultTitle, PartialResultTitle, FailureResultTitle)
        );
    }

    /// <summary>
    /// Re-evaluates the start command when an operation starts or stops running.
    /// </summary>
    /// <param name="sender">The session.</param>
    /// <param name="e">The changed property.</param>
    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OperationSession.Phase))
        {
            NotifyStartCanExecuteChanged();
        }
    }

    /// <summary>
    /// Remembers the paths of a successful operation and drops the password it no longer needs.
    /// </summary>
    /// <param name="sender">The session.</param>
    /// <param name="e">The event data.</param>
    private void OnOperationSucceeded(object? sender, EventArgs e)
    {
        _ = TrySaveRecentPathsAsync();
        ClearPassword();
    }

    /// <summary>
    /// Persists the paths of the completed operation so the pages can pre-fill them next time.
    /// </summary>
    /// <remarks>
    /// Remembering the last-used paths is a best-effort convenience, and the settings store absorbs
    /// its own storage failures: it must not affect the already-completed operation. The write is
    /// deliberately not cancellable — the operation has finished, and cancelling it must not discard
    /// where it ran.
    /// </remarks>
    /// <returns><see langword="true"/> if the paths were persisted; otherwise <see langword="false"/>.</returns>
    private async Task<bool> TrySaveRecentPathsAsync()
    {
        var recent = await SettingsStore.LoadAsync<RecentPathSettings>(CancellationToken.None);

        var saved = await SettingsStore.SaveAsync(BuildRecentPaths(recent), CancellationToken.None);

        return saved.IsSuccess;
    }
}
