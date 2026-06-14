using Avalonia.Media;
using BackupZCrypt.Application.Orchestrators.Interfaces;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects.Manifest;
using BackupZCrypt.Desktop.Resources;
using BackupZCrypt.Desktop.Services.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BackupZCrypt.Desktop.ViewModels;

/// <summary>
/// Adds automatic backup detection on top of the operation engine: whenever the backup path changes,
/// the manifest is inspected to tell the user whether a password is required before the operation starts.
/// </summary>
/// <param name="orchestrator">The orchestrator that executes backup operations.</param>
/// <param name="settingsService">The service that reads and persists user settings.</param>
/// <param name="filePicker">The folder/file picker service.</param>
/// <param name="manifestService">The service used to detect the kind of manifest at a backup path.</param>
public abstract partial class ExistingBackupViewModelBase(
    IBackupOrchestrator orchestrator,
    ISettingsService settingsService,
    IFilePickerService filePicker,
    IManifestService manifestService
) : OperationViewModelBase(orchestrator, settingsService, filePicker)
{
    private static readonly IBrush PositiveBrush = new SolidColorBrush(Color.Parse("#3FB68B"));
    private static readonly IBrush NegativeBrush = new SolidColorBrush(Color.Parse("#E5B458"));

    private int detectionVersion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ManifestKind DetectedKind { get; set; } = ManifestKind.Missing;

    [ObservableProperty]
    public partial bool HasDetection { get; set; }

    [ObservableProperty]
    public partial string DetectionMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial IBrush DetectionBrush { get; set; } = new SolidColorBrush(Color.Parse("#9AA1B5"));

    [ObservableProperty]
    public partial string DetectionIcon { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsPasswordRequired { get; set; }

    /// <summary>
    /// Gets the path that points at the existing backup (the source for a restore, the destination for an update).
    /// </summary>
    protected abstract string BackupPath { get; }

    /// <summary>
    /// Determines whether the operation can start, additionally requiring a detected chunked manifest
    /// and a password when the manifest is encrypted.
    /// </summary>
    /// <returns><see langword="true"/> when the operation may begin; otherwise <see langword="false"/>.</returns>
    protected override bool CanStart()
    {
        return base.CanStart()
            && DetectedKind is ManifestKind.Encrypted
            && (!IsPasswordRequired || Password.Length > 0);
    }

    /// <summary>
    /// Re-runs backup detection whenever a relevant path changes.
    /// </summary>
    protected override void HandlePathChanged()
    {
        _ = RefreshDetectionAsync();
    }

    private async Task RefreshDetectionAsync()
    {
        var version = Interlocked.Increment(ref detectionVersion);
        var path = BackupPath;

        if (string.IsNullOrWhiteSpace(path))
        {
            HasDetection = false;
            DetectedKind = ManifestKind.Missing;
            IsPasswordRequired = false;
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

        DetectedKind = kind;
        HasDetection = true;
        IsPasswordRequired = kind == ManifestKind.Encrypted;

        (DetectionMessage, DetectionBrush, DetectionIcon) = kind switch
        {
            ManifestKind.Encrypted => (Strings.DetectEncrypted, PositiveBrush, "🔒"),
            _ => (Strings.DetectMissing, NegativeBrush, "⚠"),
        };

        NotifyStartCanExecuteChanged();
    }
}
