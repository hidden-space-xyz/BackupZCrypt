using Avalonia.Media;
using BackupZCrypt.Application.Orchestrators.Interfaces;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects.Manifest;
using BackupZCrypt.Desktop.Resources;
using BackupZCrypt.Desktop.Services.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BackupZCrypt.Desktop.ViewModels;

// Adds automatic backup detection on top of the operation engine: whenever the
// backup path changes, the manifest is inspected to tell the user whether a
// password is required before they even start the operation.
public abstract partial class ExistingBackupViewModelBase(
    IBackupOrchestrator orchestrator,
    ISettingsService settingsService,
    IFilePickerService filePicker,
    IManifestService manifestService
) : OperationViewModelBase(orchestrator, settingsService, filePicker)
{
    private static readonly IBrush PositiveBrush = new SolidColorBrush(Color.Parse("#3FB68B"));
    private static readonly IBrush NeutralBrush = new SolidColorBrush(Color.Parse("#9AA1B5"));
    private static readonly IBrush NegativeBrush = new SolidColorBrush(Color.Parse("#E5B458"));

    private int detectionVersion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private string password = string.Empty;

    [ObservableProperty]
    private ManifestKind detectedKind = ManifestKind.Missing;

    [ObservableProperty]
    private bool hasDetection;

    [ObservableProperty]
    private string detectionMessage = string.Empty;

    [ObservableProperty]
    private IBrush detectionBrush = new SolidColorBrush(Color.Parse("#9AA1B5"));

    [ObservableProperty]
    private string detectionIcon = string.Empty;

    [ObservableProperty]
    private bool isPasswordRequired;

    // The path that points at the existing backup (source for restore,
    // destination for update).
    protected abstract string BackupPath { get; }

    protected override bool CanStart()
    {
        return base.CanStart()
            && DetectedKind is ManifestKind.Encrypted or ManifestKind.UnencryptedChunked
            && (!IsPasswordRequired || Password.Length > 0);
    }

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

        // A newer detection request superseded this one while awaiting.
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
            ManifestKind.UnencryptedChunked => (Strings.DetectUnencrypted, NeutralBrush, "🔓"),
            ManifestKind.PlainCopy => (Strings.DetectPlain, NegativeBrush, "📄"),
            _ => (Strings.DetectMissing, NegativeBrush, "⚠"),
        };

        NotifyStartCanExecuteChanged();
    }
}
