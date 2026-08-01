using BackupZCrypt.Application.Orchestrators.Interfaces;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects.Backup;
using BackupZCrypt.Desktop.Resources;
using BackupZCrypt.Desktop.Services.Interfaces;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.ValueObjects.Backup;

using CommunityToolkit.Mvvm.Input;

namespace BackupZCrypt.Desktop.ViewModels;

/// <summary>
/// ViewModel for the verify page: checks that an existing backup is complete and undamaged by
/// decrypting and re-hashing every chunk, without writing any files. Only the backup location and
/// its password are required.
/// </summary>
/// <param name="orchestrator">The orchestrator that executes the verify operation.</param>
/// <param name="settingsService">The service that reads and persists user settings.</param>
/// <param name="filePicker">The folder picker service.</param>
/// <param name="manifestService">The service used to detect the kind of manifest at the backup path.</param>
internal sealed partial class VerifyBackupViewModel(
    IBackupOrchestrator orchestrator,
    ISettingsService settingsService,
    IFilePickerService filePicker,
    IManifestService manifestService
) : ExistingBackupViewModelBase(orchestrator, settingsService, filePicker, manifestService)
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
    /// Builds the verify request. The encryption and key-derivation values only signal that a
    /// password is involved; the actual algorithms are read from the manifest during verification.
    /// </summary>
    /// <param name="proceedOnWarnings">Whether the operation should continue past warnings.</param>
    /// <returns>The configured <see cref="BackupRequest"/>.</returns>
    protected override BackupRequest CreateRequest(bool proceedOnWarnings)
    {
        return new BackupRequest(
            SourcePath,
            string.Empty,
            Password,
            Password,
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.Argon2id,
            BackupOperation.Verify,
            CompressionMode.None,
            proceedOnWarnings
        );
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
