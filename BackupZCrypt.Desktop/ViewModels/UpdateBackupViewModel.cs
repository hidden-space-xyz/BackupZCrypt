using BackupZCrypt.Application.Backup;
using BackupZCrypt.Application.Settings;
using BackupZCrypt.Desktop.ViewModels.Operations;

using CommunityToolkit.Mvvm.Input;

namespace BackupZCrypt.Desktop.ViewModels;

/// <summary>
/// ViewModel for the update page: re-scans the source path and updates the existing backup stored at
/// the destination path.
/// </summary>
/// <param name="operations">The backup operations the page runs.</param>
/// <param name="settingsStore">The store that loads and saves the recently used paths.</param>
/// <param name="filePicker">The folder picker service.</param>
internal sealed partial class UpdateBackupViewModel(
    IBackupOperations operations,
    ISettingsStore settingsStore,
    IFilePickerService filePicker
) : ExistingBackupViewModelBase(operations, settingsStore, filePicker)
{
    /// <summary>
    /// Gets the backup location, which for an update is the destination path.
    /// </summary>
    protected override string BackupPath => DestinationPath;

    /// <summary>
    /// Seeds the source and destination paths from the most recently used values when they are still empty.
    /// </summary>
    /// <param name="recent">The recently used paths.</param>
    protected override void ApplyRecentPaths(RecentPathSettings recent)
    {
        if (string.IsNullOrWhiteSpace(SourcePath) && recent.LastSourcePath is not null)
        {
            SourcePath = recent.LastSourcePath;
        }

        if (string.IsNullOrWhiteSpace(DestinationPath) && recent.LastDestinationPath is not null)
        {
            DestinationPath = recent.LastDestinationPath;
        }
    }

    /// <summary>
    /// Captures the request from the current inputs and returns the operation that runs it.
    /// </summary>
    /// <returns>The execution of the operation with the captured request.</returns>
    protected override OperationExecution CaptureExecution()
    {
        UpdateBackupRequest request = new(SourcePath, DestinationPath, Password);

        return (proceedOnWarnings, progress, cancellationToken) =>
            Operations.UpdateAsync(
                request with { ProceedOnWarnings = proceedOnWarnings },
                progress,
                cancellationToken
            );
    }

    /// <summary>
    /// Lets the user browse for the folder whose current contents feed the update.
    /// </summary>
    /// <returns>A task that completes once the folder picker has been dismissed.</returns>
    [RelayCommand]
    private Task PickSourceFolderAsync()
    {
        return PickFolderIntoAsync(path => SourcePath = path);
    }

    /// <summary>
    /// Lets the user browse for the folder holding the backup to update.
    /// </summary>
    /// <returns>A task that completes once the folder picker has been dismissed.</returns>
    [RelayCommand]
    private Task PickBackupFolderAsync()
    {
        return PickFolderIntoAsync(path => DestinationPath = path);
    }
}
