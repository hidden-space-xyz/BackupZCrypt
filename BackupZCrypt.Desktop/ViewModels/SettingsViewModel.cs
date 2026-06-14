using System.Collections.ObjectModel;
using System.Globalization;

using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.Utilities.Formatters;
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
    private readonly IBackupBenchmarkService benchmarkService;
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

    [ObservableProperty]
    public partial string BenchmarkDataAmount { get; set; }

    [ObservableProperty]
    public partial DataSizeUnitOption SelectedDataUnit { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunBenchmarkCommand))]
    public partial bool IsBenchmarkRunning { get; set; }

    [ObservableProperty]
    public partial bool ShowBenchmarkResult { get; set; }

    [ObservableProperty]
    public partial string BenchmarkDurationText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string BenchmarkThroughputText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasBenchmarkError { get; set; }

    [ObservableProperty]
    public partial string BenchmarkError { get; set; } = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsViewModel"/> class, building the selectable
    /// algorithm and language option lists from the registered strategies.
    /// </summary>
    /// <param name="settingsService">The service that reads and persists user settings.</param>
    /// <param name="benchmarkService">The service that estimates backup processing time.</param>
    /// <param name="encryptionStrategies">The available encryption algorithm strategies.</param>
    /// <param name="keyDerivationStrategies">The available key-derivation algorithm strategies.</param>
    /// <param name="compressionStrategies">The available compression strategies.</param>
    public SettingsViewModel(
        ISettingsService settingsService,
        IBackupBenchmarkService benchmarkService,
        IEnumerable<IEncryptionAlgorithmStrategy> encryptionStrategies,
        IEnumerable<IKeyDerivationAlgorithmStrategy> keyDerivationStrategies,
        IEnumerable<ICompressionStrategy> compressionStrategies
    )
    {
        this.settingsService = settingsService;
        this.benchmarkService = benchmarkService;

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

        DataUnitOptions =
        [
            new DataSizeUnitOption("MB", 1024L * 1024L),
            new DataSizeUnitOption("GB", 1024L * 1024L * 1024L),
            new DataSizeUnitOption("TB", 1024L * 1024L * 1024L * 1024L),
        ];

        SelectedEncryption = EncryptionOptions[0];
        SelectedKeyDerivation = KeyDerivationOptions[0];
        SelectedCompression = CompressionOptions[0];
        SelectedLanguage = LanguageOptions[0];
        BenchmarkDataAmount = "100";
        SelectedDataUnit = DataUnitOptions[1];

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
    /// Gets the selectable data-size units (MB, GB, TB) used by the benchmark.
    /// </summary>
    public ObservableCollection<DataSizeUnitOption> DataUnitOptions { get; }

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

    private bool CanRunBenchmark()
    {
        return !IsBenchmarkRunning;
    }

    [RelayCommand(CanExecute = nameof(CanRunBenchmark))]
    private async Task RunBenchmarkAsync()
    {
        ShowBenchmarkResult = false;
        HasBenchmarkError = false;
        BenchmarkError = string.Empty;

        if (!TryParseDataBytes(out var dataBytes))
        {
            BenchmarkError = Strings.BenchmarkInvalidAmount;
            HasBenchmarkError = true;
            return;
        }

        IsBenchmarkRunning = true;

        try
        {
            BenchmarkRequest request = new(
                SelectedEncryption.Id,
                SelectedKeyDerivation.Id,
                SelectedCompression.Id,
                dataBytes
            );

            var estimate = await Task.Run(() => benchmarkService.EstimateAsync(request));

            BenchmarkDurationText = string.Format(
                CultureInfo.CurrentCulture,
                Strings.BenchmarkResultDurationFormat,
                DurationFormatter.Format(estimate.EstimatedDuration)
            );

            BenchmarkThroughputText = string.Format(
                CultureInfo.CurrentCulture,
                Strings.BenchmarkResultThroughputFormat,
                ByteSizeFormatter.Format((long)estimate.ThroughputBytesPerSecond)
            );

            ShowBenchmarkResult = true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            BenchmarkError = Strings.BenchmarkFailed;
            HasBenchmarkError = true;
        }
        finally
        {
            IsBenchmarkRunning = false;
        }
    }

    private bool TryParseDataBytes(out long dataBytes)
    {
        dataBytes = 0;

        if (
            !double.TryParse(
                BenchmarkDataAmount,
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.CurrentCulture,
                out var amount
            )
            || amount <= 0
            || double.IsNaN(amount)
            || double.IsInfinity(amount)
        )
        {
            return false;
        }

        var totalBytes = amount * SelectedDataUnit.BytesPerUnit;
        if (totalBytes is < 1 or >= long.MaxValue)
        {
            return false;
        }

        dataBytes = (long)totalBytes;
        return true;
    }
}
