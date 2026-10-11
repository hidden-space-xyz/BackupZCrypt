using BackupZCrypt.Application.Backup;
using BackupZCrypt.Application.Common;
using BackupZCrypt.Application.Settings;
using BackupZCrypt.Desktop.Services.Interfaces;

using CommunityToolkit.Mvvm.Input;

namespace BackupZCrypt.Desktop.ViewModels;

/// <summary>
/// ViewModel for the restore page: reads an existing backup from the source path and writes the
/// recovered files to the destination path.
/// </summary>
/// <param name="operations">The backup operations the page runs.</param>
/// <param name="settingsStore">The store that loads and saves the recently used paths.</param>
/// <param name="filePicker">The folder picker service.</param>
internal sealed partial class RestoreBackupViewModel(
    IBackupOperations operations,
    ISettingsStore settingsStore,
    IFilePickerService filePicker
) : ExistingBackupViewModelBase(operations, settingsStore, filePicker)
{
    /// <summary>
    /// Gets the backup location, which for a restore is the source path.
    /// </summary>
    protected override string BackupPath => SourcePath;

    /// <summary>
    /// Seeds the source path from the most recently used destination when it is still empty.
    /// </summary>
    /// <param name="recent">The recently used paths.</param>
    protected override void ApplyRecentPaths(RecentPathSettings recent)
    {
        if (string.IsNullOrWhiteSpace(SourcePath) && recent.LastDestinationPath is not null)
        {
            SourcePath = recent.LastDestinationPath;
        }
    }

    /// <summary>
    /// Records the restored backup as the most recent backup location, leaving the remembered source
    /// folder untouched: the restore target is neither a backup nor a folder that was backed up, and
    /// remembering it in either role would pre-fill the other pages with the wrong folder.
    /// </summary>
    /// <param name="current">The currently persisted recent paths.</param>
    /// <returns>The recent paths to save.</returns>
    protected override RecentPathSettings BuildRecentPaths(RecentPathSettings current)
    {
        ArgumentNullException.ThrowIfNull(current);

        return current with { LastDestinationPath = SourcePath };
    }

    /// <summary>
    /// Builds the restore-backup request from the current inputs and runs it.
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
        return Operations.RestoreAsync(
            new RestoreBackupRequest(SourcePath, DestinationPath, Password, proceedOnWarnings),
            progress,
            cancellationToken
        );
    }

    /// <summary>
    /// Lets the user browse for the folder holding the backup to restore.
    /// </summary>
    /// <returns>A task that completes once the folder picker has been dismissed.</returns>
    [RelayCommand]
    private Task PickBackupFolderAsync()
    {
        return PickFolderIntoAsync(path => SourcePath = path);
    }

    /// <summary>
    /// Lets the user browse for the folder that will receive the recovered files.
    /// </summary>
    /// <returns>A task that completes once the folder picker has been dismissed.</returns>
    [RelayCommand]
    private Task PickDestinationFolderAsync()
    {
        return PickFolderIntoAsync(path => DestinationPath = path);
    }
}
