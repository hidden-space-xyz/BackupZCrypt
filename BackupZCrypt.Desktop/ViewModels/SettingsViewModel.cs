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

public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService settingsService;
    private bool loaded;
    private string? savedLanguageCode;

    [ObservableProperty]
    private EncryptionOption selectedEncryption;

    [ObservableProperty]
    private KeyDerivationOption selectedKeyDerivation;

    [ObservableProperty]
    private CompressionOption selectedCompression;

    [ObservableProperty]
    private LanguageOption selectedLanguage;

    [ObservableProperty]
    private bool showSavedNotice;

    [ObservableProperty]
    private bool showRestartNote;

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
            new EncryptionOption(
                EncryptionAlgorithm.None,
                Strings.NoneEncryptionName,
                Strings.NoneEncryptionDescription
            ),
            .. encryptionStrategies
                .Where(static s => s.Id != EncryptionAlgorithm.None)
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

        selectedEncryption = EncryptionOptions[0];
        selectedKeyDerivation = KeyDerivationOptions[0];
        selectedCompression = CompressionOptions[0];
        selectedLanguage = LanguageOptions[0];

        SettingsFilePath = settingsService.GetFilePath<BackupCreationSettings>();
    }

    public ObservableCollection<EncryptionOption> EncryptionOptions { get; }

    public ObservableCollection<KeyDerivationOption> KeyDerivationOptions { get; }

    public ObservableCollection<CompressionOption> CompressionOptions { get; }

    public ObservableCollection<LanguageOption> LanguageOptions { get; }

    public string SettingsFilePath { get; }

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
            // Leave the defaults selected when settings cannot be read.
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
