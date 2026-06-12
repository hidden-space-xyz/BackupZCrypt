using Avalonia.Media;
using BackupZCrypt.Application.Orchestrators.Interfaces;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects.Backup;
using BackupZCrypt.Desktop.Messages;
using BackupZCrypt.Desktop.Resources;
using BackupZCrypt.Desktop.Services.Interfaces;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Backup;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace BackupZCrypt.Desktop.ViewModels;

public sealed partial class CreateBackupViewModel : OperationViewModelBase
{
    private const int GeneratedPasswordLength = 50;

    private static readonly IBrush WeakBrush = new SolidColorBrush(Color.Parse("#E2606C"));
    private static readonly IBrush FairBrush = new SolidColorBrush(Color.Parse("#E5B458"));
    private static readonly IBrush GoodBrush = new SolidColorBrush(Color.Parse("#7CB46B"));
    private static readonly IBrush StrongBrush = new SolidColorBrush(Color.Parse("#3FB68B"));

    private readonly IPasswordService passwordService;
    private readonly IClipboardService clipboardService;
    private readonly IReadOnlyDictionary<EncryptionAlgorithm, string> encryptionNames;
    private readonly IReadOnlyDictionary<KeyDerivationAlgorithm, string> keyDerivationNames;
    private readonly IReadOnlyDictionary<CompressionMode, string> compressionNames;

    // Effective configuration, owned by the Settings page and refreshed on every
    // navigation to this page.
    private EncryptionAlgorithm encryptionAlgorithm = EncryptionAlgorithm.Aes;
    private KeyDerivationAlgorithm keyDerivationAlgorithm = KeyDerivationAlgorithm.Argon2id;
    private CompressionMode compressionMode = CompressionMode.None;

    [ObservableProperty]
    private bool isEncryptionEnabled = true;

    [ObservableProperty]
    private string encryptionSummary = string.Empty;

    [ObservableProperty]
    private string keyDerivationSummary = string.Empty;

    [ObservableProperty]
    private string compressionSummary = string.Empty;

    [ObservableProperty]
    private string password = string.Empty;

    [ObservableProperty]
    private string confirmPassword = string.Empty;

    [ObservableProperty]
    private bool revealPassword;

    [ObservableProperty]
    private double strengthScore;

    [ObservableProperty]
    private string strengthDescription = string.Empty;

    [ObservableProperty]
    private IBrush strengthBrush = Brushes.Gray;

    [ObservableProperty]
    private bool hasStrength;

    [ObservableProperty]
    private bool showGeneratedNotice;

    [ObservableProperty]
    private bool showCopiedNotice;

    public CreateBackupViewModel(
        IBackupOrchestrator orchestrator,
        ISettingsService settingsService,
        IFilePickerService filePicker,
        IClipboardService clipboardService,
        IPasswordService passwordService,
        IEnumerable<IEncryptionAlgorithmStrategy> encryptionStrategies,
        IEnumerable<IKeyDerivationAlgorithmStrategy> keyDerivationStrategies,
        IEnumerable<ICompressionStrategy> compressionStrategies
    )
        : base(orchestrator, settingsService, filePicker)
    {
        this.passwordService = passwordService;
        this.clipboardService = clipboardService;

        encryptionNames = encryptionStrategies.ToDictionary(
            static s => s.Id,
            static s => s.DisplayName
        );
        keyDerivationNames = keyDerivationStrategies.ToDictionary(
            static s => s.Id,
            static s => s.DisplayName
        );
        compressionNames = compressionStrategies.ToDictionary(
            static s => s.Id,
            static s => s.DisplayName
        );

        UpdateConfigurationSummary();
    }

    public override async Task OnNavigatedToAsync()
    {
        await base.OnNavigatedToAsync();

        // Don't change the configuration shown while an operation is in flight.
        if (IsRunning)
        {
            return;
        }

        // Always reflect the latest defaults configured on the Settings page.
        try
        {
            var defaults = await SettingsService.GetOrCreateAsync<BackupCreationSettings>();
            encryptionAlgorithm = defaults.EncryptionAlgorithm;
            keyDerivationAlgorithm = defaults.KeyDerivationAlgorithm;
            compressionMode = defaults.CompressionMode;
            UpdateConfigurationSummary();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Keep the previously shown configuration when settings cannot be read.
        }
    }

    protected override void ApplyRecentPaths(RecentPathSettings recent)
    {
        if (string.IsNullOrWhiteSpace(SourcePath) && recent.LastSourcePath is not null)
        {
            SourcePath = recent.LastSourcePath;
        }
    }

    protected override BackupRequest CreateRequest(bool proceedOnWarnings)
    {
        var encryptionEnabled = IsEncryptionEnabled;

        return new BackupRequest(
            SourcePath,
            DestinationPath,
            encryptionEnabled ? Password : string.Empty,
            encryptionEnabled ? ConfirmPassword : string.Empty,
            encryptionAlgorithm,
            keyDerivationAlgorithm,
            BackupOperation.Create,
            compressionMode,
            proceedOnWarnings
        );
    }

    [RelayCommand]
    private static void OpenSettings()
    {
        WeakReferenceMessenger.Default.Send(new NavigateToPageMessage(typeof(SettingsViewModel)));
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
    private async Task PickSourceFileAsync()
    {
        var path = await FilePicker.PickFileAsync(Strings.PickFileTitle);
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
        RevealPassword = true;
        ShowGeneratedNotice = true;
        ShowCopiedNotice = false;
    }

    [RelayCommand]
    private async Task CopyPasswordAsync()
    {
        if (Password.Length == 0)
        {
            return;
        }

        await clipboardService.SetTextAsync(Password);
        ShowCopiedNotice = true;
    }

    private void UpdateConfigurationSummary()
    {
        IsEncryptionEnabled = encryptionAlgorithm != EncryptionAlgorithm.None;

        EncryptionSummary =
            encryptionAlgorithm == EncryptionAlgorithm.None
                ? Strings.NoneEncryptionName
                : encryptionNames.GetValueOrDefault(
                    encryptionAlgorithm,
                    Strings.NoneEncryptionName
                );

        KeyDerivationSummary = keyDerivationNames.GetValueOrDefault(
            keyDerivationAlgorithm,
            string.Empty
        );

        CompressionSummary =
            compressionMode == CompressionMode.None
                ? Strings.NoneCompressionName
                : compressionNames.GetValueOrDefault(compressionMode, Strings.NoneCompressionName);
    }

    partial void OnPasswordChanged(string value)
    {
        ShowCopiedNotice = false;

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
        StrengthDescription = analysis.Description;
        StrengthBrush = analysis.Strength switch
        {
            PasswordStrength.VeryWeak or PasswordStrength.Weak => WeakBrush,
            PasswordStrength.Fair => FairBrush,
            PasswordStrength.Good => GoodBrush,
            _ => StrongBrush,
        };
    }
}
