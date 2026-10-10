using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Application.ValueObjects.Backup;
using BackupZCrypt.Application.ValueObjects.Settings;
using BackupZCrypt.Desktop.Services.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Backup;

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
    /// Builds the update-backup request from the current inputs and runs it.
    /// </summary>
    /// <param name="proceedOnWarnings">Whether the operation should continue past warnings.</param>
    /// <param name="progress">The sink that receives incremental status updates.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The outcome of the operation.</returns>
    protected override Task<Result<BackupOutcome>> ExecuteOperationAsync(
        bool proceedOnWarnings,
        IProgress<BackupStatus> progress,
        CancellationToken cancellationToken
    )
    {
        return Operations.UpdateAsync(
            new UpdateBackupRequest(SourcePath, DestinationPath, Password, proceedOnWarnings),
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
