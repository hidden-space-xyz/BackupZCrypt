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

public sealed partial class UpdateBackupViewModel(
    IBackupOrchestrator orchestrator,
    ISettingsService settingsService,
    IFilePickerService filePicker,
    IManifestService manifestService
) : ExistingBackupViewModelBase(orchestrator, settingsService, filePicker, manifestService)
{
    // For an update, the backup is the destination.
    protected override string BackupPath => DestinationPath;

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

    protected override BackupRequest CreateRequest(bool proceedOnWarnings)
    {
        // The update reads algorithm and key derivation from the existing
        // manifest; the values here only signal whether a password is involved.
        return new BackupRequest(
            SourcePath,
            DestinationPath,
            IsPasswordRequired ? Password : string.Empty,
            IsPasswordRequired ? Password : string.Empty,
            IsPasswordRequired ? EncryptionAlgorithm.Aes : EncryptionAlgorithm.None,
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
