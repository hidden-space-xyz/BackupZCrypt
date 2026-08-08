using BackupZCrypt.Application.Commands;
using BackupZCrypt.Application.Commands.Interfaces;
using BackupZCrypt.Application.Queries;
using BackupZCrypt.Application.Queries.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Application.ValueObjects.Backup;
using BackupZCrypt.Application.ValueObjects.Manifest;
using BackupZCrypt.Application.ValueObjects.Settings;
using BackupZCrypt.Desktop.Resources;
using BackupZCrypt.Desktop.Services.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Backup;

using CommunityToolkit.Mvvm.Input;

namespace BackupZCrypt.Desktop.ViewModels;

/// <summary>
/// ViewModel for the verify page: checks that an existing backup is complete and undamaged by
/// decrypting and re-hashing every chunk, without writing any files. Only the backup location and
/// its password are required.
/// </summary>
/// <param name="verifyBackup">The handler that answers the verify-backup query.</param>
/// <param name="recentPathsQuery">The handler that loads the recently used paths.</param>
/// <param name="saveRecentPathsCommand">The handler that persists the recently used paths.</param>
/// <param name="filePicker">The folder picker service.</param>
/// <param name="detectManifestKind">The handler that detects the kind of manifest at the backup path.</param>
internal sealed partial class VerifyBackupViewModel(
    IQueryHandler<VerifyBackupQuery, Result<BackupOutcome>> verifyBackup,
    IQueryHandler<GetSettingsQuery<RecentPathSettings>, RecentPathSettings> recentPathsQuery,
    ICommandHandler<SaveSettingsCommand<RecentPathSettings>, Result> saveRecentPathsCommand,
    IFilePickerService filePicker,
    IQueryHandler<DetectManifestKindQuery, ManifestKind> detectManifestKind
) : ExistingBackupViewModelBase(recentPathsQuery, saveRecentPathsCommand, filePicker, detectManifestKind)
{
    /// <summary>
    /// Gets the backup location, which for a verification is the source path.
    /// </summary>
    protected override string BackupPath => SourcePath;

    /// <summary>
    /// Gets the result title shown when every file passes the integrity check.
    /// </summary>
    protected override string SuccessResultTitle => Strings.VerifySuccessTitle;

    /// <summary>
    /// Gets the result title shown when some files fail the integrity check.
    /// </summary>
    protected override string PartialResultTitle => Strings.VerifyPartialTitle;

    /// <summary>
    /// Gets the result title shown when the backup could not be verified at all.
    /// </summary>
    protected override string FailureResultTitle => Strings.VerifyFailureTitle;

    /// <summary>
    /// Seeds the backup path from the most recently used backup location when it is still empty.
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
    /// Gets a value indicating that verification needs no destination path: only a backup location with
    /// a detected encrypted manifest and its password are required, which the base start gate enforces.
    /// </summary>
    protected override bool RequiresDestination => false;

    /// <summary>
    /// Records the verified backup location as the most recent backup path without clearing the
    /// remembered source, since verification has no destination of its own.
    /// </summary>
    /// <param name="current">The currently persisted recent paths.</param>
    /// <returns>The recent paths to save.</returns>
    protected override RecentPathSettings BuildRecentPaths(RecentPathSettings current)
    {
        return current with { LastDestinationPath = SourcePath };
    }

    /// <summary>
    /// Builds the verify-backup query from the current inputs and dispatches it to its handler.
    /// Verification skips validation and never raises advisory warnings, so the proceed flag carries
    /// no information for it and is deliberately ignored.
    /// </summary>
    /// <param name="proceedOnWarnings">Ignored: verification never stops at warnings.</param>
    /// <param name="progress">The sink that receives incremental status updates.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The outcome of the verification.</returns>
    protected override Task<Result<BackupOutcome>> ExecuteOperationAsync(
        bool proceedOnWarnings,
        IProgress<BackupStatus> progress,
        CancellationToken cancellationToken
    )
    {
        var query = new VerifyBackupQuery(SourcePath, Password)
        {
            Progress = progress,
        };

        return verifyBackup.HandleAsync(query, cancellationToken);
    }

    /// <summary>
    /// Lets the user browse for the folder holding the backup to verify.
    /// </summary>
    /// <returns>A task that completes once the folder picker has been dismissed.</returns>
    [RelayCommand]
    private Task PickBackupFolderAsync()
    {
        return PickFolderIntoAsync(path => SourcePath = path);
    }
}
