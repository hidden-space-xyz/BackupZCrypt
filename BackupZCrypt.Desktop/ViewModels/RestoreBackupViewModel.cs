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
/// ViewModel for the restore page: reads an existing backup from the source path and writes the
/// recovered files to the destination path.
/// </summary>
/// <param name="restoreBackup">The handler that executes the restore-backup command.</param>
/// <param name="recentPathsQuery">The handler that loads the recently used paths.</param>
/// <param name="saveRecentPathsCommand">The handler that persists the recently used paths.</param>
/// <param name="filePicker">The folder picker service.</param>
/// <param name="detectManifestKind">The handler that detects the kind of manifest at the backup path.</param>
internal sealed partial class RestoreBackupViewModel(
    ICommandHandler<RestoreBackupCommand, Result<BackupOutcome>> restoreBackup,
    IQueryHandler<GetSettingsQuery<RecentPathSettings>, RecentPathSettings> recentPathsQuery,
    ICommandHandler<SaveSettingsCommand<RecentPathSettings>, Result> saveRecentPathsCommand,
    IFilePickerService filePicker,
    IQueryHandler<DetectManifestKindQuery, ManifestKind> detectManifestKind
) : ExistingBackupViewModelBase(recentPathsQuery, saveRecentPathsCommand, filePicker, detectManifestKind)
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
    /// Builds the restore-backup command from the current inputs and dispatches it to its handler.
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
        var command = new RestoreBackupCommand(SourcePath, DestinationPath, Password, proceedOnWarnings)
        {
            Progress = progress,
        };

        return restoreBackup.HandleAsync(command, cancellationToken);
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
