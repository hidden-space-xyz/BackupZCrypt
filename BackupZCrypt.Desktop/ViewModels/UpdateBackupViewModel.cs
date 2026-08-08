using BackupZCrypt.Application.Commands;
using BackupZCrypt.Application.Commands.Interfaces;
using BackupZCrypt.Application.Queries;
using BackupZCrypt.Application.Queries.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Application.ValueObjects.Backup;
using BackupZCrypt.Application.ValueObjects.Manifest;
using BackupZCrypt.Application.ValueObjects.Settings;
using BackupZCrypt.Desktop.Services.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Backup;

using CommunityToolkit.Mvvm.Input;

namespace BackupZCrypt.Desktop.ViewModels;

/// <summary>
/// ViewModel for the update page: re-scans the source path and updates the existing backup stored at
/// the destination path.
/// </summary>
/// <param name="updateBackup">The handler that executes the update-backup command.</param>
/// <param name="recentPathsQuery">The handler that loads the recently used paths.</param>
/// <param name="saveRecentPathsCommand">The handler that persists the recently used paths.</param>
/// <param name="filePicker">The folder picker service.</param>
/// <param name="detectManifestKind">The handler that detects the kind of manifest at the backup path.</param>
internal sealed partial class UpdateBackupViewModel(
    ICommandHandler<UpdateBackupCommand, Result<BackupOutcome>> updateBackup,
    IQueryHandler<GetSettingsQuery<RecentPathSettings>, RecentPathSettings> recentPathsQuery,
    ICommandHandler<SaveSettingsCommand<RecentPathSettings>, Result> saveRecentPathsCommand,
    IFilePickerService filePicker,
    IQueryHandler<DetectManifestKindQuery, ManifestKind> detectManifestKind
) : ExistingBackupViewModelBase(recentPathsQuery, saveRecentPathsCommand, filePicker, detectManifestKind)
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
    /// Builds the update-backup command from the current inputs and dispatches it to its handler.
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
        var command = new UpdateBackupCommand(SourcePath, DestinationPath, Password, proceedOnWarnings)
        {
            Progress = progress,
        };

        return updateBackup.HandleAsync(command, cancellationToken);
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
