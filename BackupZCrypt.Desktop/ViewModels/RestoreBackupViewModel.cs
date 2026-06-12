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

public sealed partial class RestoreBackupViewModel(
    IBackupOrchestrator orchestrator,
    ISettingsService settingsService,
    IFilePickerService filePicker,
    IManifestService manifestService
) : ExistingBackupViewModelBase(orchestrator, settingsService, filePicker, manifestService)
{
    // For a restore, the backup is the source.
    protected override string BackupPath => SourcePath;

    protected override void ApplyRecentPaths(RecentPathSettings recent)
    {
        if (string.IsNullOrWhiteSpace(SourcePath) && recent.LastDestinationPath is not null)
        {
            SourcePath = recent.LastDestinationPath;
        }
    }

    protected override BackupRequest CreateRequest(bool proceedOnWarnings)
    {
        // Algorithm and key derivation are read from the manifest preamble during
        // the restore; they only signal here whether a password is involved.
        return new BackupRequest(
            SourcePath,
            DestinationPath,
            IsPasswordRequired ? Password : string.Empty,
            IsPasswordRequired ? Password : string.Empty,
            IsPasswordRequired ? EncryptionAlgorithm.Aes : EncryptionAlgorithm.None,
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
