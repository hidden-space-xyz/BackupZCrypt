using BackupZCrypt.Application.Orchestrators.Interfaces;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects.Backup;
using BackupZCrypt.Application.ValueObjects.Manifest;
using BackupZCrypt.Desktop.Resources;
using BackupZCrypt.Desktop.Services.Interfaces;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.ValueObjects.Backup;
using CommunityToolkit.Mvvm.Input;

namespace BackupZCrypt.Desktop.ViewModels;

/// <summary>
/// ViewModel for the update page: re-scans the source path and updates the existing backup stored at
/// the destination path.
/// </summary>
/// <param name="orchestrator">The orchestrator that executes the update operation.</param>
/// <param name="settingsService">The service that reads and persists user settings.</param>
/// <param name="filePicker">The folder picker service.</param>
/// <param name="manifestService">The service used to detect the kind of manifest at the backup path.</param>
public sealed partial class UpdateBackupViewModel(
    IBackupOrchestrator orchestrator,
    ISettingsService settingsService,
    IFilePickerService filePicker,
    IManifestService manifestService
) : ExistingBackupViewModelBase(orchestrator, settingsService, filePicker, manifestService)
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
    /// Builds the update request. The encryption and key-derivation values only signal whether a
    /// password is involved; the actual algorithms are read from the existing manifest during the update.
    /// </summary>
    /// <param name="proceedOnWarnings">Whether the operation should continue past warnings.</param>
    /// <returns>The configured <see cref="BackupRequest"/>.</returns>
    protected override BackupRequest CreateRequest(bool proceedOnWarnings)
    {
        return new BackupRequest(
            SourcePath,
            DestinationPath,
            Password,
            Password,
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.Argon2id,
            BackupOperation.Update,
            CompressionMode.None,
            proceedOnWarnings
        );
    }

    [RelayCommand]
    private async Task PickSourceFolderAsync()
    {
        var path = await FilePicker.PickFolderAsync(Strings.PickFolderTitle);
        if (path is not null)
        {
            SourcePath = path;
        }
    }

    [RelayCommand]
    private async Task PickBackupFolderAsync()
    {
        var path = await FilePicker.PickFolderAsync(Strings.PickFolderTitle);
        if (path is not null)
        {
            DestinationPath = path;
        }
    }
}
