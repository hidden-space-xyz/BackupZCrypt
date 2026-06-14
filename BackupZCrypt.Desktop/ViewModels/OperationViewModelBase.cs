using System.Collections.ObjectModel;
using System.Globalization;

using BackupZCrypt.Application.Orchestrators.Interfaces;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.Utilities.Formatters;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Application.ValueObjects.Backup;
using BackupZCrypt.Desktop.Resources;
using BackupZCrypt.Desktop.Services;
using BackupZCrypt.Desktop.Services.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Backup;
using BackupZCrypt.Domain.ValueObjects.Localization;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BackupZCrypt.Desktop.ViewModels;

/// <summary>
/// Shared engine for the create/update/restore pages: request execution with progress reporting,
/// cancellation, the warnings confirmation flow and the final result panel. Subclasses only describe
/// how to build the request.
/// </summary>
/// <param name="orchestrator">The orchestrator that executes backup operations.</param>
/// <param name="settingsService">The service that reads and persists user settings.</param>
/// <param name="filePicker">The folder/file picker service.</param>
public abstract partial class OperationViewModelBase(
    IBackupOrchestrator orchestrator,
    ISettingsService settingsService,
    IFilePickerService filePicker
) : ViewModelBase
{
    private CancellationTokenSource? operationCts;
    private bool recentPathsLoaded;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial string SourcePath { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial string DestinationPath { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    public partial double ProgressValue { get; set; }

    [ObservableProperty]
    public partial string ProgressText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ElapsedText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasResult { get; set; }

    [ObservableProperty]
    public partial bool ResultIsSuccess { get; set; }

    [ObservableProperty]
    public partial string ResultTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ResultFiles { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ResultDuration { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ResultSize { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasResultDetails { get; set; }

    [ObservableProperty]
    public partial bool ShowWarnings { get; set; }

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
    /// Gets a value indicating whether no operation is currently running.
    /// </summary>
    public bool IsIdle => !IsRunning;

    /// <summary>
    /// Gets the folder/file picker service.
    /// </summary>
    protected IFilePickerService FilePicker { get; } = filePicker;

    /// <summary>
    /// Gets the service that reads and persists user settings.
    /// </summary>
    protected ISettingsService SettingsService { get; } = settingsService;

    /// <summary>
    /// Loads and applies the most recently used paths the first time the page is shown.
    /// </summary>
    /// <returns>A task that completes once the recent paths have been applied.</returns>
    public override async Task OnNavigatedToAsync()
    {
        if (recentPathsLoaded)
        {
            return;
        }

        recentPathsLoaded = true;

        try
        {
            var recent = await SettingsService.GetOrCreateAsync<RecentPathSettings>();
            ApplyRecentPaths(recent);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Ignore
        }
    }

    /// <summary>
    /// Builds the operation request for the current inputs.
    /// </summary>
    /// <param name="proceedOnWarnings">Whether the operation should continue past warnings.</param>
    /// <returns>The configured <see cref="BackupRequest"/>.</returns>
    protected abstract BackupRequest CreateRequest(bool proceedOnWarnings);

    /// <summary>
    /// Applies the recently used paths to the page inputs. The default implementation does nothing.
    /// </summary>
    /// <param name="recent">The recently used paths.</param>
    protected virtual void ApplyRecentPaths(RecentPathSettings recent)
    {
    }

    /// <summary>
    /// Determines whether the operation can start, requiring that no operation is running and both
    /// source and destination paths are set.
    /// </summary>
    /// <returns><see langword="true"/> when the operation may begin; otherwise <see langword="false"/>.</returns>
    protected virtual bool CanStart()
    {
        return !IsRunning
            && !string.IsNullOrWhiteSpace(SourcePath)
            && !string.IsNullOrWhiteSpace(DestinationPath);
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

    partial void OnSourcePathChanged(string value)
    {
        HandlePathChanged();
    }

    partial void OnDestinationPathChanged(string value)
    {
        HandlePathChanged();
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task StartAsync()
    {
        return RunAsync(proceedOnWarnings: false);
    }

    [RelayCommand]
    private void CancelOperation()
    {
        operationCts?.Cancel();
    }

    [RelayCommand]
    private Task ContinueAnywayAsync()
    {
        ShowWarnings = false;
        return RunAsync(proceedOnWarnings: true);
    }

    [RelayCommand]
    private void DismissWarnings()
    {
        ShowWarnings = false;
        Warnings.Clear();
    }

    [RelayCommand]
    private void DismissResult()
    {
        HasResult = false;
    }

    private async Task RunAsync(bool proceedOnWarnings)
    {
        ResetState();
        IsRunning = true;

        using CancellationTokenSource cts = new();
        operationCts = cts;

        try
        {
            var request = CreateRequest(proceedOnWarnings);
            Progress<BackupStatus> progress = new(ReportProgress);

            var result = await Task.Run(
                () => orchestrator.ExecuteAsync(request, progress, cts.Token)
            );

            HandleResult(result, proceedOnWarnings);
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
            Errors.Add(ex.Message);
            ShowErrors = true;
        }
        finally
        {
            IsRunning = false;
            operationCts = null;
        }
    }

    private void ReportProgress(BackupStatus status)
    {
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

    private void HandleResult(Result<BackupResult> result, bool proceedOnWarnings)
    {
        if (!result.IsSuccess)
        {
            ShowFailure(result.Errors);
            return;
        }

        var operation = result.Value;

        if (operation.HasErrors && operation.TotalFiles == 0 && operation.ProcessedFiles == 0)
        {
            ShowFailure(operation.Errors);
            return;
        }

        if (operation.HasWarnings && !proceedOnWarnings && operation.ProcessedFiles == 0)
        {
            foreach (var warning in operation.Warnings)
            {
                Warnings.Add(MessageLocalizer.Localize(warning));
            }

            ShowWarnings = true;
            return;
        }

        HasResult = true;
        ResultIsSuccess = operation.IsSuccess;
        ResultTitle = operation.IsSuccess ? Strings.ResultSuccessTitle : Strings.ResultPartialTitle;

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
            _ = SaveRecentPathsAsync();
        }
    }

    private void ShowFailure(IEnumerable<LocalizableMessage> errors)
    {
        HasResult = true;
        ResultIsSuccess = false;
        ResultTitle = Strings.ResultErrorTitle;

        foreach (var error in errors)
        {
            Errors.Add(MessageLocalizer.Localize(error));
        }

        ShowErrors = Errors.Count > 0;
    }

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
        ProgressText = string.Empty;
        ElapsedText = string.Empty;
    }

    private async Task SaveRecentPathsAsync()
    {
        try
        {
            var recent = await SettingsService.GetOrCreateAsync<RecentPathSettings>();

            await SettingsService.SaveAsync(
                recent with
                {
                    LastSourcePath = SourcePath,
                    LastDestinationPath = DestinationPath,
                }
            );
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Ignore
        }
    }
}
