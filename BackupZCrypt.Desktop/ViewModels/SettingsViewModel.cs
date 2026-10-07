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
    /// A value indicating whether the selections are being set from the stored settings, during which
    /// they are not user edits.
    /// </summary>
    private bool applyingStoredSettings;

    /// <summary>
    /// The algorithm defaults as last persisted, used to tell whether the page holds unsaved changes.
    /// </summary>
    private BackupCreationSettings savedDefaults = BackupCreationSettings.DefaultValue;

    /// <summary>
    /// The language code as last persisted, used to tell whether the page holds unsaved changes and
    /// whether the user changed the language and therefore needs to restart.
    /// </summary>
    private string? savedLanguageCode;

    /// <summary>
    /// The language code that was in effect when the application started, which a language change
    /// only replaces after a restart.
    /// </summary>
    private string? startupLanguageCode;

    /// <summary>
    /// A value indicating whether <see cref="startupLanguageCode"/> has been captured.
    /// </summary>
    private bool startupLanguageCaptured;

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
    /// Gets or sets a value indicating whether the "settings could not be saved" error is shown.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowSaveError { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the selections differ from the persisted settings.
    /// </summary>
    [ObservableProperty]
    public partial bool HasUnsavedChanges { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the unsaved-changes note is shown, which gives way to
    /// the save error when both apply.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowUnsavedNote { get; set; }

    /// <summary>
    /// Gets or sets the formatted estimate for a single large file produced by the benchmark.
    /// </summary>
    [ObservableProperty]
    public partial string BenchmarkLargeFileText { get; set; } = string.Empty;

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
                AlgorithmMetadataProvider.GetName(CompressionMode.None),
                AlgorithmMetadataProvider.GetSummary(CompressionMode.None)
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

        applyingStoredSettings = true;
        SelectedEncryption = EncryptionOptions[0];
        SelectedKeyDerivation = KeyDerivationOptions[0];
        SelectedCompression = CompressionOptions[0];
        SelectedLanguage = LanguageOptions[0];
        applyingStoredSettings = false;
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
    /// Loads the persisted defaults and language preference every time the page is shown, so the page
    /// always shows the settings new backups will actually use; selections that were never saved are
    /// discarded when the user leaves the page.
    /// </summary>
    /// <remarks>
    /// The handlers absorb a failure to read the stored settings into the defaults, which match the
    /// selections the constructor made, so the page always offers a valid configuration to save.
    /// </remarks>
    /// <returns>A task that completes once the settings have been loaded.</returns>
    public override async Task OnNavigatedToAsync()
    {
        var defaults = await creationDefaultsQuery.HandleAsync(
            new GetSettingsQuery<BackupCreationSettings>(),
            CancellationToken.None
        );
        var language = await languageQuery.HandleAsync(
            new GetSettingsQuery<LanguageSettings>(),
            CancellationToken.None
        );

        if (!startupLanguageCaptured)
        {
            startupLanguageCode = language.LanguageCode;
            startupLanguageCaptured = true;
        }

        applyingStoredSettings = true;

        try
        {
            SelectedEncryption =
                EncryptionOptions.FirstOrDefault(o => o.Id == defaults.EncryptionAlgorithm)
                ?? EncryptionOptions.First(o => o.Id == BackupCreationSettings.DefaultValue.EncryptionAlgorithm);
            SelectedKeyDerivation =
                KeyDerivationOptions.FirstOrDefault(o => o.Id == defaults.KeyDerivationAlgorithm)
                ?? KeyDerivationOptions.First(o =>
                    o.Id == BackupCreationSettings.DefaultValue.KeyDerivationAlgorithm
                );
            SelectedCompression =
                CompressionOptions.FirstOrDefault(o => o.Id == defaults.CompressionMode)
                ?? CompressionOptions.First(o => o.Id == BackupCreationSettings.DefaultValue.CompressionMode);

            SelectedLanguage =
                LanguageOptions.FirstOrDefault(o =>
                    string.Equals(o.Code, language.LanguageCode, StringComparison.OrdinalIgnoreCase)
                ) ?? LanguageOptions[0];
        }
        finally
        {
            applyingStoredSettings = false;
        }

        savedDefaults = CurrentDefaults();
        savedLanguageCode = SelectedLanguage.Code;
        ShowSavedNotice = false;
        ShowSaveError = false;
        UpdateUnsavedChanges();
    }

    /// <summary>
    /// Persists the selected algorithm defaults and language, and reports whether a restart is needed
    /// for the new language to take effect.
    /// </summary>
    /// <remarks>
    /// A failed write is reported on the page; the handlers already absorb the failure itself into the
    /// result contract.
    /// </remarks>
    /// <returns>A task that completes once the settings have been written.</returns>
    [RelayCommand]
    private async Task SaveAsync()
    {
        ShowSavedNotice = false;
        ShowSaveError = false;

        var settings = CurrentDefaults();

        var defaultsResult = await saveCreationDefaults.HandleAsync(
            new SaveSettingsCommand<BackupCreationSettings>(settings),
            CancellationToken.None
        );

        if (!defaultsResult.IsSuccess)
        {
            ShowSaveError = true;
            return;
        }

        savedDefaults = settings;

        var languageResult = await saveLanguage.HandleAsync(
            new SaveSettingsCommand<LanguageSettings>(new LanguageSettings(SelectedLanguage.Code)),
            CancellationToken.None
        );

        if (!languageResult.IsSuccess)
        {
            ShowSaveError = true;
            UpdateUnsavedChanges();
            return;
        }

        savedLanguageCode = SelectedLanguage.Code;

        ShowRestartNote = !string.Equals(
            startupLanguageCode,
            SelectedLanguage.Code,
            StringComparison.OrdinalIgnoreCase
        );

        UpdateUnsavedChanges();
        ShowSavedNotice = true;
    }

    /// <summary>
    /// Builds the algorithm defaults the current selections describe.
    /// </summary>
    /// <returns>The selected defaults.</returns>
    private BackupCreationSettings CurrentDefaults()
    {
        return new BackupCreationSettings(
            SelectedEncryption.Id,
            SelectedKeyDerivation.Id,
            SelectedCompression.Id
        );
    }

    /// <summary>
    /// Re-evaluates whether the selections differ from the persisted settings.
    /// </summary>
    private void UpdateUnsavedChanges()
    {
        HasUnsavedChanges =
            CurrentDefaults() != savedDefaults
            || !string.Equals(savedLanguageCode, SelectedLanguage.Code, StringComparison.OrdinalIgnoreCase);
        ShowUnsavedNote = HasUnsavedChanges && !ShowSaveError;
    }

    /// <summary>
    /// Hides the unsaved-changes note while the save error is shown, and shows it again afterwards.
    /// </summary>
    /// <param name="value">Whether the save error is shown.</param>
    partial void OnShowSaveErrorChanged(bool value)
    {
        ShowUnsavedNote = HasUnsavedChanges && !value;
    }

    /// <summary>
    /// Treats a selection made by the user as an edit: the saved notice no longer applies to it.
    /// </summary>
    private void OnSelectionEdited()
    {
        if (applyingStoredSettings)
        {
            return;
        }

        ShowSavedNotice = false;
        ShowSaveError = false;
        UpdateUnsavedChanges();
    }

    /// <summary>
    /// Reacts to a change of the selected encryption algorithm.
    /// </summary>
    /// <param name="value">The newly selected option.</param>
    partial void OnSelectedEncryptionChanged(EncryptionOption value)
    {
        OnSelectionEdited();
    }

    /// <summary>
    /// Reacts to a change of the selected key-derivation algorithm.
    /// </summary>
    /// <param name="value">The newly selected option.</param>
    partial void OnSelectedKeyDerivationChanged(KeyDerivationOption value)
    {
        OnSelectionEdited();
    }

    /// <summary>
    /// Reacts to a change of the selected compression mode.
    /// </summary>
    /// <param name="value">The newly selected option.</param>
    partial void OnSelectedCompressionChanged(CompressionOption value)
    {
        OnSelectionEdited();
    }

    /// <summary>
    /// Reacts to a change of the selected language.
    /// </summary>
    /// <param name="value">The newly selected option.</param>
    partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        OnSelectionEdited();
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

        if (!TryParseDataBytes(out var dataBytes, out var amount))
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
                string.Create(
                    CultureInfo.CurrentCulture,
                    $"{amount:0.###} {SelectedDataUnit.Name}"
                ),
                DurationFormatter.Format(estimate.Value.EstimatedDuration)
            );

            BenchmarkThroughputText = string.Format(
                CultureInfo.CurrentCulture,
                Strings.BenchmarkResultThroughputFormat,
                ByteSizeFormatter.Format((long)estimate.Value.ThroughputBytesPerSecond)
            );

            BenchmarkLargeFileText = estimate.Value.LargeFileEstimatedDuration is { } largeFileDuration
                ? string.Format(
                    CultureInfo.CurrentCulture,
                    Strings.BenchmarkLargeFileFormat,
                    DurationFormatter.Format(largeFileDuration),
                    ByteSizeFormatter.Format((long)estimate.Value.LargeFileThroughputBytesPerSecond)
                )
                : string.Empty;

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
    /// <remarks>
    /// Either a point or a comma is accepted as the decimal separator, whatever the language. A single
    /// separator only groups thousands when it is the language's own group separator followed by
    /// exactly three digits, as in "1,000" in English or "1.000" in Spanish; otherwise "1.5" in
    /// Spanish would be read as fifteen and silently estimate ten times the amount the user meant.
    /// The amount the estimate was made for is shown with the result, so any reading is visible.
    /// </remarks>
    /// <param name="dataBytes">Receives the byte count, or zero when the entry is not usable.</param>
    /// <param name="amount">Receives the parsed amount in the selected unit.</param>
    /// <returns><see langword="true"/> if a usable byte count was produced; otherwise <see langword="false"/>.</returns>
    private bool TryParseDataBytes(out long dataBytes, out double amount)
    {
        dataBytes = 0;
        amount = 0;

        var entered = (BenchmarkDataAmount ?? string.Empty).Trim();
        var separatorIndex = entered.IndexOfAny(['.', ',']);

        if (separatorIndex >= 0)
        {
            var separator = entered[separatorIndex];
            var groupsThousands =
                string.Equals(
                    separator.ToString(),
                    CultureInfo.CurrentCulture.NumberFormat.NumberGroupSeparator,
                    StringComparison.Ordinal
                )
                && entered.Length - separatorIndex - 1 == 3;

            entered = (groupsThousands ? entered.Remove(separatorIndex, 1) : entered).Replace(',', '.');
        }

        if (
            entered.Count(static c => c is '.') > 1
            || !double.TryParse(
                entered,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out amount
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
