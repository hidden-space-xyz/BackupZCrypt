using Avalonia.Media;

using BackupZCrypt.Application.Orchestrators.Interfaces;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects.Backup;
using BackupZCrypt.Desktop.Services;
using BackupZCrypt.Desktop.Services.Interfaces;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.ValueObjects.Backup;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BackupZCrypt.Desktop.ViewModels;

/// <summary>
/// ViewModel for the create-backup page: collects the source and destination, manages the password
/// (entry, reveal, generation, copy and strength feedback) and reflects the algorithm defaults from settings.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="CreateBackupViewModel"/> class.
/// </remarks>
/// <param name="orchestrator">The orchestrator that executes the backup operation.</param>
/// <param name="settingsService">The service that reads and persists user settings.</param>
/// <param name="filePicker">The folder/file picker service.</param>
/// <param name="clipboardService">The clipboard service used to copy a generated password.</param>
/// <param name="passwordService">The service that generates passwords and analyzes their strength.</param>
public sealed partial class CreateBackupViewModel(
    IBackupOrchestrator orchestrator,
    ISettingsService settingsService,
    IFilePickerService filePicker,
    IClipboardService clipboardService,
    IPasswordService passwordService
    ) : OperationViewModelBase(orchestrator, settingsService, filePicker)
{
    private const int GeneratedPasswordLength = 50;

    private const int MinPasswordLength = 8;
    private const int MaxPasswordLength = 1000;

    private static readonly IBrush WeakBrush = new SolidColorBrush(Color.Parse("#E2606C"));
    private static readonly IBrush FairBrush = new SolidColorBrush(Color.Parse("#E5B458"));
    private static readonly IBrush GoodBrush = new SolidColorBrush(Color.Parse("#7CB46B"));
    private static readonly IBrush StrongBrush = new SolidColorBrush(Color.Parse("#3FB68B"));
    private EncryptionAlgorithm encryptionAlgorithm = EncryptionAlgorithm.Aes;
    private KeyDerivationAlgorithm keyDerivationAlgorithm = KeyDerivationAlgorithm.Argon2id;
    private CompressionMode compressionMode = CompressionMode.None;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyPasswordCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial string ConfirmPassword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool RevealPassword { get; set; }

    [ObservableProperty]
    public partial double StrengthScore { get; set; }

    [ObservableProperty]
    public partial string StrengthDescription { get; set; } = string.Empty;

    [ObservableProperty]
    public partial IBrush StrengthBrush { get; set; } = Brushes.Gray;

    [ObservableProperty]
    public partial bool HasStrength { get; set; }

    /// <summary>
    /// Refreshes the encryption state from the latest settings defaults, unless an operation is
    /// currently in flight.
    /// </summary>
    /// <returns>A task that completes once the encryption state has been refreshed.</returns>
    public override async Task OnNavigatedToAsync()
    {
        await base.OnNavigatedToAsync();

        if (IsRunning)
        {
            return;
        }

        try
        {
            var defaults = await SettingsService.GetOrCreateAsync<BackupCreationSettings>();
            encryptionAlgorithm = Enum.IsDefined(defaults.EncryptionAlgorithm)
                ? defaults.EncryptionAlgorithm
                : EncryptionAlgorithm.Aes;
            keyDerivationAlgorithm = defaults.KeyDerivationAlgorithm;
            compressionMode = defaults.CompressionMode;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
        }
    }

    /// <summary>
    /// Seeds the source path from the most recently used source when it is still empty.
    /// </summary>
    /// <param name="recent">The recently used paths.</param>
    protected override void ApplyRecentPaths(RecentPathSettings recent)
    {
        if (string.IsNullOrWhiteSpace(SourcePath) && recent.LastSourcePath is not null)
        {
            SourcePath = recent.LastSourcePath;
        }
    }

    /// <summary>
    /// Builds the create-backup request from the current source, destination, password and configured algorithms.
    /// </summary>
    /// <param name="proceedOnWarnings">Whether the operation should continue past warnings.</param>
    /// <returns>The configured <see cref="BackupRequest"/>.</returns>
    protected override BackupRequest CreateRequest(bool proceedOnWarnings)
    {
        return new BackupRequest(
            SourcePath,
            DestinationPath,
            Password,
            ConfirmPassword,
            encryptionAlgorithm,
            keyDerivationAlgorithm,
            BackupOperation.Create,
            compressionMode,
            proceedOnWarnings
        );
    }

    /// <summary>
    /// Determines whether the backup can start, additionally requiring a valid, confirmed password so
    /// the user cannot start a backup that would immediately fail password validation.
    /// </summary>
    /// <returns><see langword="true"/> when the backup may begin; otherwise <see langword="false"/>.</returns>
    protected override bool CanStart()
    {
        return base.CanStart() && IsPasswordValid();
    }

    private bool IsPasswordValid()
    {
        return Password.Length >= MinPasswordLength
            && Password.Length <= MaxPasswordLength
            && string.Equals(Password.Trim(), Password, StringComparison.Ordinal)
            && string.Equals(Password, ConfirmPassword, StringComparison.Ordinal);
    }

    [RelayCommand]
    private Task PickSourceFolderAsync()
    {
        return PickFolderIntoAsync(path => SourcePath = path);
    }

    [RelayCommand]
    private Task PickDestinationFolderAsync()
    {
        return PickFolderIntoAsync(path => DestinationPath = path);
    }

    [RelayCommand]
    private void GeneratePassword()
    {
        const PasswordGenerationOptions options =
            PasswordGenerationOptions.IncludeUppercase
            | PasswordGenerationOptions.IncludeLowercase
            | PasswordGenerationOptions.IncludeNumbers
            | PasswordGenerationOptions.IncludeSpecialCharacters;

        var generated = passwordService.GeneratePassword(GeneratedPasswordLength, options);

        Password = generated;
        ConfirmPassword = generated;
        RevealPassword = false;
    }

    private bool CanCopyPassword => Password.Length > 0;

    [RelayCommand(CanExecute = nameof(CanCopyPassword))]
    private async Task CopyPasswordAsync()
    {
        await clipboardService.SetTextAsync(Password);
    }

    partial void OnPasswordChanged(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            HasStrength = false;
            StrengthScore = 0;
            StrengthDescription = string.Empty;
            return;
        }

        var analysis = passwordService.AnalyzePasswordStrength(value);

        HasStrength = true;
        StrengthScore = analysis.Score;
        StrengthDescription = PasswordStrengthFormatter.Format(analysis);
        StrengthBrush = analysis.Strength switch
        {
            PasswordStrength.VeryWeak or PasswordStrength.Weak => WeakBrush,
            PasswordStrength.Fair => FairBrush,
            PasswordStrength.Good => GoodBrush,
            _ => StrongBrush,
        };
    }
}
