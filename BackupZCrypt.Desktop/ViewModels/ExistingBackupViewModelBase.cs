using BackupZCrypt.Application.Backup;
using BackupZCrypt.Application.Manifest;
using BackupZCrypt.Application.Settings;
using BackupZCrypt.Desktop.Resources;

using CommunityToolkit.Mvvm.ComponentModel;

namespace BackupZCrypt.Desktop.ViewModels;

/// <summary>
/// Adds automatic backup detection on top of the operation engine: whenever the backup path changes,
/// the manifest is inspected to tell the user whether a password is required before the operation starts.
/// </summary>
/// <param name="operations">The backup operations the page runs, which also inspect the backup path.</param>
/// <param name="settingsStore">The store that loads and saves the recently used paths.</param>
/// <param name="filePicker">The folder picker service.</param>
internal abstract partial class ExistingBackupViewModelBase(
    IBackupOperations operations,
    ISettingsStore settingsStore,
    IFilePickerService filePicker
) : OperationViewModelBase(settingsStore, filePicker)
{
    /// <summary>
    /// The counter that stamps each detection run, so a slow result for an older path is discarded
    /// instead of overwriting the state of the path the user has since typed.
    /// </summary>
    private int detectionVersion;

    /// <summary>
    /// Gets the backup operations the page runs and inspects the backup path with.
    /// </summary>
    protected IBackupOperations Operations { get; } = operations;

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
    /// Gets or sets the warning explaining why no usable backup was found at the backup path.
    /// </summary>
    [ObservableProperty]
    public partial string DetectionMessage { get; set; } = string.Empty;

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
    /// The backup operations absorb a probe that fails into no backup found, since detection only
    /// decides what the page shows; the operation itself still validates the path.
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

        var kind = await Operations.DetectManifestKindAsync(path, CancellationToken.None);

        if (version != Volatile.Read(ref detectionVersion))
        {
            return;
        }

        IsBackupDetected = kind is ManifestKind.Encrypted;
        HasDetection = !IsBackupDetected;
        DetectionMessage = DescribeDetection(kind);

        NotifyStartCanExecuteChanged();
    }

    /// <summary>
    /// Returns the warning that explains a detection result other than a usable backup.
    /// </summary>
    /// <param name="kind">The detected manifest kind.</param>
    /// <returns>The localized warning, or an empty string for a usable backup.</returns>
    private static string DescribeDetection(ManifestKind kind)
    {
        return kind switch
        {
            ManifestKind.Encrypted => string.Empty,
            ManifestKind.PathNotFound => Strings.DetectPathNotFound,
            ManifestKind.NotADirectory => Strings.DetectNotADirectory,
            ManifestKind.Damaged => Strings.DetectDamaged,
            ManifestKind.Unsupported => Strings.DetectUnsupported,
            ManifestKind.Missing => Strings.DetectMissing,
            _ => Strings.DetectMissing,
        };
    }
}
