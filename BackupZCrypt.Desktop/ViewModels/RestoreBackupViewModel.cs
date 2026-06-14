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
/// ViewModel for the restore page: reads an existing backup from the source path and writes the
/// recovered files to the destination path.
/// </summary>
/// <param name="orchestrator">The orchestrator that executes the restore operation.</param>
/// <param name="settingsService">The service that reads and persists user settings.</param>
/// <param name="filePicker">The folder picker service.</param>
/// <param name="manifestService">The service used to detect the kind of manifest at the backup path.</param>
public sealed partial class RestoreBackupViewModel(
    IBackupOrchestrator orchestrator,
    ISettingsService settingsService,
    IFilePickerService filePicker,
    IManifestService manifestService
) : ExistingBackupViewModelBase(orchestrator, settingsService, filePicker, manifestService)
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
    /// Builds the restore request. The encryption and key-derivation values only signal whether a
    /// password is involved; the actual algorithms are read from the manifest preamble during the restore.
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
            BackupOperation.Restore,
            CompressionMode.None,
            proceedOnWarnings
        );
    }

    [RelayCommand]
    private async Task PickBackupFolderAsync()
    {
        var path = await FilePicker.PickFolderAsync(Strings.PickFolderTitle);
        if (path is not null)
        {
            SourcePath = path;
        }
    }

    [RelayCommand]
    private async Task PickDestinationFolderAsync()
    {
        var path = await FilePicker.PickFolderAsync(Strings.PickFolderTitle);
        if (path is not null)
        {
            DestinationPath = path;
        }
    }
}
