using BackupZCrypt.Domain.Services.Interfaces;
using BackupZCrypt.Application.Orchestrators.Interfaces;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects.Manifest;
using BackupZCrypt.Desktop.Services.Interfaces;

using CommunityToolkit.Mvvm.ComponentModel;

namespace BackupZCrypt.Desktop.ViewModels;

/// <summary>
/// Adds automatic backup detection on top of the operation engine: whenever the backup path changes,
/// the manifest is inspected to tell the user whether a password is required before the operation starts.
/// </summary>
/// <param name="orchestrator">The orchestrator that executes backup operations.</param>
/// <param name="settingsService">The service that reads and persists user settings.</param>
/// <param name="filePicker">The folder picker service.</param>
/// <param name="manifestService">The service used to detect the kind of manifest at the backup path.</param>
internal abstract partial class ExistingBackupViewModelBase(
    IBackupOrchestrator orchestrator,
    ISettingsService settingsService,
    IFilePickerService filePicker,
    IManifestService manifestService
) : OperationViewModelBase(orchestrator, settingsService, filePicker)
{
    /// <summary>
    /// The counter that stamps each detection run, so a slow result for an older path is discarded
    /// instead of overwriting the state of the path the user has since typed.
    /// </summary>
    private int detectionVersion;

    /// <summary>
    /// Gets or sets a value indicating whether a readable backup was detected at the backup path.
    /// </summary>
    [ObservableProperty]
    public partial bool IsBackupDetected { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the no-backup-found warning is shown.
    /// </summary>
    [ObservableProperty]
    public partial bool HasDetection { get; set; }

    /// <summary>
    /// Gets the path that points at the existing backup (the source for a restore, the destination for an update).
    /// </summary>
    protected abstract string BackupPath { get; }

    /// <summary>
    /// Determines whether the operation can start, additionally requiring a detected backup and a password.
    /// </summary>
    /// <returns><see langword="true"/> if the operation may begin; otherwise <see langword="false"/>.</returns>
    protected override bool CanStart()
    {
        return base.CanStart() && IsBackupDetected && Password.Length > 0;
    }

    /// <summary>
    /// Re-runs backup detection whenever a relevant path changes.
    /// </summary>
    protected override void HandlePathChanged()
    {
        _ = RefreshDetectionAsync();
    }

    /// <summary>
    /// Inspects the manifest at <see cref="BackupPath"/> and updates the detection state, requiring a
    /// password when an encrypted manifest is found and reporting no backup otherwise.
    /// </summary>
    /// <remarks>
    /// A probe that throws is treated as no backup found, since detection only decides what the page
    /// shows; the operation itself still validates the path.
    /// </remarks>
    /// <returns>A task that completes once the detection state reflects the current path.</returns>
    private async Task RefreshDetectionAsync()
    {
        var version = Interlocked.Increment(ref detectionVersion);
        var path = BackupPath;

        if (string.IsNullOrWhiteSpace(path))
        {
            HasDetection = false;
            IsBackupDetected = false;
            NotifyStartCanExecuteChanged();
            return;
        }

        ManifestKind kind;

        try
        {
            kind = await manifestService.DetectManifestKindAsync(path);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            kind = ManifestKind.Missing;
        }

        if (version != Volatile.Read(ref detectionVersion))
        {
            return;
        }

        IsBackupDetected = kind is ManifestKind.Encrypted;
        HasDetection = !IsBackupDetected;

        NotifyStartCanExecuteChanged();
    }
}
