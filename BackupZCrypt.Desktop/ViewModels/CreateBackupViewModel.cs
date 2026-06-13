using Avalonia.Media;
using BackupZCrypt.Application.Orchestrators.Interfaces;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects.Backup;
using BackupZCrypt.Desktop.Messages;
using BackupZCrypt.Desktop.Resources;
using BackupZCrypt.Desktop.Services;
using BackupZCrypt.Desktop.Services.Interfaces;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Backup;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace BackupZCrypt.Desktop.ViewModels;

/// <summary>
/// ViewModel for the create-backup page: collects the source and destination, manages the password
/// (entry, reveal, generation, copy and strength feedback) and reflects the algorithm defaults from settings.
/// </summary>
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

    private EncryptionAlgorithm encryptionAlgorithm = EncryptionAlgorithm.Aes;
    private KeyDerivationAlgorithm keyDerivationAlgorithm = KeyDerivationAlgorithm.Argon2id;
    private CompressionMode compressionMode = CompressionMode.None;

    [ObservableProperty]
    public partial bool IsEncryptionEnabled { get; set; } = true;

    [ObservableProperty]
    public partial string EncryptionSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string KeyDerivationSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CompressionSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
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

    [ObservableProperty]
    public partial bool ShowGeneratedNotice { get; set; }

    [ObservableProperty]
    public partial bool ShowCopiedNotice { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CreateBackupViewModel"/> class.
    /// </summary>
    /// <param name="orchestrator">The orchestrator that executes the backup operation.</param>
    /// <param name="settingsService">The service that reads and persists user settings.</param>
    /// <param name="filePicker">The folder/file picker service.</param>
    /// <param name="clipboardService">The clipboard service used to copy a generated password.</param>
    /// <param name="passwordService">The service that generates passwords and analyzes their strength.</param>
    /// <param name="encryptionStrategies">The available encryption algorithm strategies.</param>
    /// <param name="keyDerivationStrategies">The available key-derivation algorithm strategies.</param>
    /// <param name="compressionStrategies">The available compression strategies.</param>
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
            static s => AlgorithmMetadataProvider.GetName(s.Id)
        );
        keyDerivationNames = keyDerivationStrategies.ToDictionary(
            static s => s.Id,
            static s => AlgorithmMetadataProvider.GetName(s.Id)
        );
        compressionNames = compressionStrategies.ToDictionary(
            static s => s.Id,
            static s => AlgorithmMetadataProvider.GetName(s.Id)
        );

        UpdateConfigurationSummary();
    }

    /// <summary>
    /// Refreshes the displayed algorithm configuration from the latest settings defaults, unless an
    /// operation is currently in flight.
    /// </summary>
    /// <returns>A task that completes once the configuration summary has been refreshed.</returns>
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
            encryptionAlgorithm = defaults.EncryptionAlgorithm;
            keyDerivationAlgorithm = defaults.KeyDerivationAlgorithm;
            compressionMode = defaults.CompressionMode;
            UpdateConfigurationSummary();
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
