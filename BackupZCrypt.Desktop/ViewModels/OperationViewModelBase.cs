using System.Collections.ObjectModel;
using System.Globalization;
using BackupZCrypt.Application.Orchestrators.Interfaces;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.Utilities.Formatters;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Application.ValueObjects.Backup;
using BackupZCrypt.Desktop.Resources;
using BackupZCrypt.Desktop.Services.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Backup;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BackupZCrypt.Desktop.ViewModels;

// Shared engine for the create/update/restore pages: request execution with
// progress reporting, cancellation, the warnings confirmation flow and the
// final result panel. Subclasses only describe how to build the request.
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
    private string sourcePath = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private string destinationPath = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool isRunning;

    [ObservableProperty]
    private double progressValue;

    [ObservableProperty]
    private string progressText = string.Empty;

    [ObservableProperty]
    private string elapsedText = string.Empty;

    [ObservableProperty]
    private bool hasResult;

    [ObservableProperty]
    private bool resultIsSuccess;

    [ObservableProperty]
    private string resultTitle = string.Empty;

    [ObservableProperty]
    private string resultFiles = string.Empty;

    [ObservableProperty]
    private string resultDuration = string.Empty;

    [ObservableProperty]
    private string resultSize = string.Empty;

    [ObservableProperty]
    private bool hasResultDetails;

    [ObservableProperty]
    private bool showWarnings;

    [ObservableProperty]
    private bool showErrors;

    public ObservableCollection<string> Errors { get; } = [];

    public ObservableCollection<string> Warnings { get; } = [];

    public bool IsIdle => !IsRunning;

    protected IFilePickerService FilePicker { get; } = filePicker;

    protected ISettingsService SettingsService { get; } = settingsService;

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
            // Recent paths are a convenience; never block the page on them.
        }
    }

    protected abstract BackupRequest CreateRequest(bool proceedOnWarnings);

    protected virtual void ApplyRecentPaths(RecentPathSettings recent) { }

    protected virtual bool CanStart()
    {
        return !IsRunning
            && !string.IsNullOrWhiteSpace(SourcePath)
            && !string.IsNullOrWhiteSpace(DestinationPath);
    }

    protected void NotifyStartCanExecuteChanged()
    {
        StartCommand.NotifyCanExecuteChanged();
    }

    protected virtual void HandlePathChanged() { }

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

            // The orchestrator performs CPU-heavy key derivation synchronously,
            // so it runs on the thread pool to keep the UI responsive.
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

        // Validation failed before any processing happened.
        if (operation.HasErrors && operation.TotalFiles == 0 && operation.ProcessedFiles == 0)
        {
            ShowFailure(operation.Errors);
            return;
        }

        // The orchestrator stops on warnings until the user confirms.
        if (operation.HasWarnings && !proceedOnWarnings && operation.ProcessedFiles == 0)
        {
            foreach (var warning in operation.Warnings)
            {
                Warnings.Add(warning);
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
            Errors.Add(error);
        }

        ShowErrors = Errors.Count > 0;

        if (operation.IsSuccess)
        {
            _ = SaveRecentPathsAsync();
        }
    }

    private void ShowFailure(IEnumerable<string> errors)
    {
        HasResult = true;
        ResultIsSuccess = false;
        ResultTitle = Strings.ResultErrorTitle;

        foreach (var error in errors)
        {
            Errors.Add(error);
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
            // Best-effort persistence of the path history.
        }
    }
}
