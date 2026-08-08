using System.Collections.ObjectModel;
using System.Globalization;

using BackupZCrypt.Application.Commands;
using BackupZCrypt.Application.Commands.Interfaces;
using BackupZCrypt.Application.Queries;
using BackupZCrypt.Application.Queries.Interfaces;
using BackupZCrypt.Application.Utilities.Formatters;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Application.ValueObjects.Benchmark;
using BackupZCrypt.Application.ValueObjects.Settings;
using BackupZCrypt.Desktop.Models;
using BackupZCrypt.Desktop.Resources;
using BackupZCrypt.Desktop.Services;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BackupZCrypt.Desktop.ViewModels;

/// <summary>
/// ViewModel for the settings page: lets the user choose default encryption, key-derivation, and
/// compression algorithms plus the UI language, and persists those choices.
/// </summary>
internal sealed partial class SettingsViewModel : ViewModelBase
{
    /// <summary>
    /// The handler that loads the saved algorithm defaults.
    /// </summary>
    private readonly IQueryHandler<
        GetSettingsQuery<BackupCreationSettings>,
        BackupCreationSettings
    > creationDefaultsQuery;

    /// <summary>
    /// The handler that loads the saved language preference.
    /// </summary>
    private readonly IQueryHandler<GetSettingsQuery<LanguageSettings>, LanguageSettings> languageQuery;

    /// <summary>
    /// The handler that persists the algorithm defaults.
    /// </summary>
    private readonly ICommandHandler<SaveSettingsCommand<BackupCreationSettings>, Result> saveCreationDefaults;

    /// <summary>
    /// The handler that persists the language preference.
    /// </summary>
    private readonly ICommandHandler<SaveSettingsCommand<LanguageSettings>, Result> saveLanguage;

    /// <summary>
    /// The handler that estimates how long a backup of a given size would take.
    /// </summary>
    private readonly IQueryHandler<EstimateBackupBenchmarkQuery, Result<BenchmarkEstimate>> estimateBenchmark;

    /// <summary>
    /// A value indicating whether the stored settings have already been applied, so returning to the
    /// page never discards edits the user has not saved yet.
    /// </summary>
    private bool loaded;

    /// <summary>
    /// The language code that was persisted when the page loaded, used to tell whether the user changed
    /// the language and therefore needs to restart.
    /// </summary>
    private string? savedLanguageCode;

    /// <summary>
    /// Gets or sets the selected default encryption algorithm.
    /// </summary>
    [ObservableProperty]
    public partial EncryptionOption SelectedEncryption { get; set; }

    /// <summary>
    /// Gets or sets the selected default key-derivation algorithm.
    /// </summary>
    [ObservableProperty]
    public partial KeyDerivationOption SelectedKeyDerivation { get; set; }

    /// <summary>
    /// Gets or sets the selected default compression mode.
    /// </summary>
    [ObservableProperty]
    public partial CompressionOption SelectedCompression { get; set; }

    /// <summary>
    /// Gets or sets the selected UI language.
    /// </summary>
    [ObservableProperty]
    public partial LanguageOption SelectedLanguage { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the "settings saved" notice is shown.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowSavedNotice { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the "restart required" note is shown after a language change.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowRestartNote { get; set; }

    /// <summary>
    /// Gets or sets the data amount, as entered by the user, used to size the benchmark.
    /// </summary>
    [ObservableProperty]
    public partial string BenchmarkDataAmount { get; set; }

    /// <summary>
    /// Gets or sets the data-size unit applied to the benchmark amount.
    /// </summary>
    [ObservableProperty]
    public partial DataSizeUnitOption SelectedDataUnit { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a benchmark is currently running.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunBenchmarkCommand))]
    public partial bool IsBenchmarkRunning { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the benchmark result is shown.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowBenchmarkResult { get; set; }

    /// <summary>
    /// Gets or sets the formatted estimated duration produced by the benchmark.
    /// </summary>
    [ObservableProperty]
    public partial string BenchmarkDurationText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the formatted estimated throughput produced by the benchmark.
    /// </summary>
    [ObservableProperty]
    public partial string BenchmarkThroughputText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether a benchmark error message is shown.
    /// </summary>
    [ObservableProperty]
    public partial bool HasBenchmarkError { get; set; }

    /// <summary>
    /// Gets or sets the benchmark error message shown to the user.
    /// </summary>
    [ObservableProperty]
    public partial string BenchmarkError { get; set; } = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsViewModel"/> class, building the selectable
    /// algorithm and language option lists from the registered strategies.
    /// </summary>
    /// <param name="creationDefaultsQuery">The handler that loads the saved algorithm defaults.</param>
    /// <param name="languageQuery">The handler that loads the saved language preference.</param>
    /// <param name="saveCreationDefaults">The handler that persists the algorithm defaults.</param>
    /// <param name="saveLanguage">The handler that persists the language preference.</param>
    /// <param name="settingsFilePathQuery">The handler that resolves the settings file path shown for reference.</param>
    /// <param name="estimateBenchmark">The handler that estimates backup processing time.</param>
    /// <param name="encryptionStrategies">The available encryption algorithm strategies.</param>
    /// <param name="keyDerivationStrategies">The available key-derivation algorithm strategies.</param>
    /// <param name="compressionStrategies">The available compression strategies.</param>
    public SettingsViewModel(
        IQueryHandler<GetSettingsQuery<BackupCreationSettings>, BackupCreationSettings> creationDefaultsQuery,
        IQueryHandler<GetSettingsQuery<LanguageSettings>, LanguageSettings> languageQuery,
        ICommandHandler<SaveSettingsCommand<BackupCreationSettings>, Result> saveCreationDefaults,
        ICommandHandler<SaveSettingsCommand<LanguageSettings>, Result> saveLanguage,
        ISyncQueryHandler<GetSettingsFilePathQuery<BackupCreationSettings>, string> settingsFilePathQuery,
        IQueryHandler<EstimateBackupBenchmarkQuery, Result<BenchmarkEstimate>> estimateBenchmark,
        IEnumerable<IEncryptionAlgorithmStrategy> encryptionStrategies,
        IEnumerable<IKeyDerivationAlgorithmStrategy> keyDerivationStrategies,
        IEnumerable<ICompressionStrategy> compressionStrategies
    )
    {
        ArgumentNullException.ThrowIfNull(settingsFilePathQuery);

        this.creationDefaultsQuery = creationDefaultsQuery;
        this.languageQuery = languageQuery;
        this.saveCreationDefaults = saveCreationDefaults;
        this.saveLanguage = saveLanguage;
        this.estimateBenchmark = estimateBenchmark;

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

        SettingsFilePath = settingsFilePathQuery.Handle(
            new GetSettingsFilePathQuery<BackupCreationSettings>()
        );
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
    /// <remarks>
    /// The handlers absorb a failure to read the stored settings into the defaults, which match the
    /// selections the constructor made, so the page always offers a valid configuration to save.
    /// </remarks>
    /// <returns>A task that completes once the settings have been loaded.</returns>
    public override async Task OnNavigatedToAsync()
    {
        if (loaded)
        {
            return;
        }

        loaded = true;

        var defaults = await creationDefaultsQuery.HandleAsync(
            new GetSettingsQuery<BackupCreationSettings>(),
            CancellationToken.None
        );
        var language = await languageQuery.HandleAsync(
            new GetSettingsQuery<LanguageSettings>(),
            CancellationToken.None
        );

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

    /// <summary>
    /// Persists the selected algorithm defaults and language, and reports whether a restart is needed
    /// for the new language to take effect.
    /// </summary>
    /// <remarks>
    /// A failed write only leaves the saved notice hidden; the handlers already absorb the failure
    /// itself into the result contract.
    /// </remarks>
    /// <returns>A task that completes once the settings have been written.</returns>
    [RelayCommand]
    private async Task SaveAsync()
    {
        ShowSavedNotice = false;

        BackupCreationSettings settings = new(
            SelectedEncryption.Id,
            SelectedKeyDerivation.Id,
            SelectedCompression.Id
        );

        var savedDefaults = await saveCreationDefaults.HandleAsync(
            new SaveSettingsCommand<BackupCreationSettings>(settings),
            CancellationToken.None
        );

        if (!savedDefaults.IsSuccess)
        {
            return;
        }

        var savedLanguage = await saveLanguage.HandleAsync(
            new SaveSettingsCommand<LanguageSettings>(new LanguageSettings(SelectedLanguage.Code)),
            CancellationToken.None
        );

        if (!savedLanguage.IsSuccess)
        {
            return;
        }

        ShowRestartNote = !string.Equals(
            savedLanguageCode,
            SelectedLanguage.Code,
            StringComparison.OrdinalIgnoreCase
        );

        ShowSavedNotice = true;
    }

    /// <summary>
    /// Determines whether a benchmark may start, which requires that no other benchmark is running.
    /// </summary>
    /// <returns><see langword="true"/> if a benchmark may begin; otherwise <see langword="false"/>.</returns>
    private bool CanRunBenchmark()
    {
        return !IsBenchmarkRunning;
    }

    /// <summary>
    /// Estimates, off the UI thread, how long backing up the entered amount of data would take with the
    /// selected algorithms, and shows the duration and throughput or an error.
    /// </summary>
    /// <returns>A task that completes once the estimate or its error has been shown.</returns>
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
            EstimateBackupBenchmarkQuery query = new(
                SelectedEncryption.Id,
                SelectedKeyDerivation.Id,
                SelectedCompression.Id,
                dataBytes
            );

            var estimate = await Task.Run(() => estimateBenchmark.HandleAsync(query));

            if (!estimate.IsSuccess)
            {
                BenchmarkError = Strings.BenchmarkFailed;
                HasBenchmarkError = true;
                return;
            }

            BenchmarkDurationText = string.Format(
                CultureInfo.CurrentCulture,
                Strings.BenchmarkResultDurationFormat,
                DurationFormatter.Format(estimate.Value.EstimatedDuration)
            );

            BenchmarkThroughputText = string.Format(
                CultureInfo.CurrentCulture,
                Strings.BenchmarkResultThroughputFormat,
                ByteSizeFormatter.Format((long)estimate.Value.ThroughputBytesPerSecond)
            );

            ShowBenchmarkResult = true;
        }
        finally
        {
            IsBenchmarkRunning = false;
        }
    }

    /// <summary>
    /// Converts the entered amount and unit into a byte count, rejecting entries that are not a
    /// positive finite number, that amount to less than one byte, or that do not fit in a
    /// <see cref="long"/>.
    /// </summary>
    /// <param name="dataBytes">Receives the byte count, or zero when the entry is not usable.</param>
    /// <returns><see langword="true"/> if a usable byte count was produced; otherwise <see langword="false"/>.</returns>
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
