using System.Collections.ObjectModel;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects.Backup;
using BackupZCrypt.Desktop.Models;
using BackupZCrypt.Desktop.Resources;
using BackupZCrypt.Desktop.Services;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BackupZCrypt.Desktop.ViewModels;

/// <summary>
/// ViewModel for the settings page: lets the user choose default encryption, key-derivation and
/// compression algorithms plus the UI language, and persists those choices.
/// </summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService settingsService;
    private bool loaded;
    private string? savedLanguageCode;

    [ObservableProperty]
    public partial EncryptionOption SelectedEncryption { get; set; }

    [ObservableProperty]
    public partial KeyDerivationOption SelectedKeyDerivation { get; set; }

    [ObservableProperty]
    public partial CompressionOption SelectedCompression { get; set; }

    [ObservableProperty]
    public partial LanguageOption SelectedLanguage { get; set; }

    [ObservableProperty]
    public partial bool ShowSavedNotice { get; set; }

    [ObservableProperty]
    public partial bool ShowRestartNote { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsViewModel"/> class, building the selectable
    /// algorithm and language option lists from the registered strategies.
    /// </summary>
    /// <param name="settingsService">The service that reads and persists user settings.</param>
    /// <param name="encryptionStrategies">The available encryption algorithm strategies.</param>
    /// <param name="keyDerivationStrategies">The available key-derivation algorithm strategies.</param>
    /// <param name="compressionStrategies">The available compression strategies.</param>
    public SettingsViewModel(
        ISettingsService settingsService,
        IEnumerable<IEncryptionAlgorithmStrategy> encryptionStrategies,
        IEnumerable<IKeyDerivationAlgorithmStrategy> keyDerivationStrategies,
        IEnumerable<ICompressionStrategy> compressionStrategies
    )
    {
        this.settingsService = settingsService;

        EncryptionOptions =
        [
            .. encryptionStrategies
                .OrderBy(static s => s.Id)
                .Select(static s => new EncryptionOption(
                    s.Id,
                    AlgorithmMetadataProvider.GetName(s.Id),
                    AlgorithmMetadataProvider.GetSummary(s.Id)
                )),
        ];

        KeyDerivationOptions =
        [
            .. keyDerivationStrategies
                .OrderBy(static s => s.Id)
                .Select(static s => new KeyDerivationOption(
                    s.Id,
                    AlgorithmMetadataProvider.GetName(s.Id),
                    AlgorithmMetadataProvider.GetSummary(s.Id)
                )),
        ];

        CompressionOptions =
        [
            new CompressionOption(
                CompressionMode.None,
                Strings.NoneCompressionName,
                Strings.NoneCompressionDescription
            ),
            .. compressionStrategies
                .OrderBy(static s => s.Id)
                .Select(static s => new CompressionOption(
                    s.Id,
                    AlgorithmMetadataProvider.GetName(s.Id),
                    AlgorithmMetadataProvider.GetSummary(s.Id)
                )),
        ];

        LanguageOptions =
        [
            new LanguageOption(null, Strings.LanguageSystemDefault),
            new LanguageOption("en", "English"),
            new LanguageOption("es", "Español"),
        ];
        SelectedEncryption = EncryptionOptions[0];
        SelectedKeyDerivation = KeyDerivationOptions[0];
        SelectedCompression = CompressionOptions[0];
        SelectedLanguage = LanguageOptions[0];

        SettingsFilePath = settingsService.GetFilePath<BackupCreationSettings>();
    }

    /// <summary>
    /// Gets the selectable encryption algorithm options.
    /// </summary>
    public ObservableCollection<EncryptionOption> EncryptionOptions { get; }

    /// <summary>
    /// Gets the selectable key-derivation algorithm options.
    /// </summary>
    public ObservableCollection<KeyDerivationOption> KeyDerivationOptions { get; }

    /// <summary>
    /// Gets the selectable compression mode options.
    /// </summary>
    public ObservableCollection<CompressionOption> CompressionOptions { get; }

    /// <summary>
    /// Gets the selectable UI language options.
    /// </summary>
    public ObservableCollection<LanguageOption> LanguageOptions { get; }

    /// <summary>
    /// Gets the on-disk path of the settings file, shown to the user for reference.
    /// </summary>
    public string SettingsFilePath { get; }

    /// <summary>
    /// Loads the persisted defaults and language preference the first time the page is shown.
    /// </summary>
    /// <returns>A task that completes once the settings have been loaded.</returns>
    public override async Task OnNavigatedToAsync()
    {
        if (loaded)
        {
            return;
        }

        loaded = true;

        try
        {
            var defaults = await settingsService.GetOrCreateAsync<BackupCreationSettings>();
            var language = await settingsService.GetOrCreateAsync<LanguageSettings>();

            SelectedEncryption =
                EncryptionOptions.FirstOrDefault(o => o.Id == defaults.EncryptionAlgorithm)
                ?? SelectedEncryption;
            SelectedKeyDerivation =
                KeyDerivationOptions.FirstOrDefault(o => o.Id == defaults.KeyDerivationAlgorithm)
                ?? SelectedKeyDerivation;
            SelectedCompression =
                CompressionOptions.FirstOrDefault(o => o.Id == defaults.CompressionMode)
                ?? SelectedCompression;

            savedLanguageCode = language.LanguageCode;
            SelectedLanguage =
                LanguageOptions.FirstOrDefault(o =>
                    string.Equals(o.Code, language.LanguageCode, StringComparison.OrdinalIgnoreCase)
                ) ?? LanguageOptions[0];
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Ignore
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ShowSavedNotice = false;

        try
        {
            BackupCreationSettings settings = new(
                SelectedEncryption.Id,
                SelectedKeyDerivation.Id,
                SelectedCompression.Id
            );

            await settingsService.SaveAsync(settings);
            await settingsService.SaveAsync(new LanguageSettings(SelectedLanguage.Code));

            ShowRestartNote = !string.Equals(
                savedLanguageCode,
                SelectedLanguage.Code,
                StringComparison.OrdinalIgnoreCase
            );

            ShowSavedNotice = true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ShowSavedNotice = false;
        }
    }
}
