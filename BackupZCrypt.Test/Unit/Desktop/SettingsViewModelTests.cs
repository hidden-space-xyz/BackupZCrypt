using BackupZCrypt.Application.Algorithms;
using BackupZCrypt.Application.Benchmark;
using BackupZCrypt.Application.Common;
using BackupZCrypt.Application.Settings;
using BackupZCrypt.Desktop.Presentation;
using BackupZCrypt.Desktop.Resources;
using BackupZCrypt.Desktop.ViewModels;
using BackupZCrypt.Domain.Algorithms;
using BackupZCrypt.Test.Common;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

namespace BackupZCrypt.Test.Unit.Desktop;

/// <summary>
/// Unit tests for the settings page: the option lists it builds from the algorithm catalog, the
/// reload of the stored defaults each time the page is shown, persistence and the language restart
/// note, and the benchmark amount parsing.
/// </summary>
public sealed class SettingsViewModelTests
{
    /// <summary>
    /// The substituted store the page loads and persists its algorithm defaults and language
    /// preference through, and resolves the settings file path shown for reference from.
    /// </summary>
    private readonly ISettingsStore settingsStore = Substitute.For<ISettingsStore>();

    /// <summary>
    /// The substituted service behind the benchmark estimate.
    /// </summary>
    private readonly IBackupBenchmarkService benchmarkService = Substitute.For<IBackupBenchmarkService>();

    [Fact]
    internal void Constructor_ListsTheOptionsInTheOrderTheCatalogGivesThem()
    {
        var catalog = AlgorithmCatalogs.Offering(
            [EncryptionAlgorithm.Serpent, EncryptionAlgorithm.Aes],
            [KeyDerivationAlgorithm.Scrypt, KeyDerivationAlgorithm.Argon2id],
            [CompressionMode.Zstd, CompressionMode.None]
        );

        SettingsViewModel sut = new(this.settingsStore, this.benchmarkService, catalog);

        Assert.Multiple(
            () => Assert.Equal(catalog.EncryptionAlgorithms, sut.EncryptionOptions.Select(static option => option.Id)),
            () => Assert.Equal(catalog.KeyDerivationAlgorithms, sut.KeyDerivationOptions.Select(static option => option.Id)),
            () => Assert.Equal(catalog.CompressionModes, sut.CompressionOptions.Select(static option => option.Id))
        );
    }

    [Fact]
    internal async Task OnNavigatedToAsync_StoredAlgorithmsThisBuildDoesNotOffer_SelectTheDefaults()
    {
        StubStoredSettings(
            new BackupCreationSettings(EncryptionAlgorithm.Serpent, KeyDerivationAlgorithm.Scrypt, CompressionMode.ZstdBest),
            LanguageSettings.DefaultValue
        );
        _ = this.settingsStore.GetFilePath<BackupCreationSettings>().Returns("settings-path.json");
        SettingsViewModel sut = new(
            this.settingsStore,
            this.benchmarkService,
            AlgorithmCatalogs.Offering(
                [EncryptionAlgorithm.Aes, EncryptionAlgorithm.Twofish],
                [KeyDerivationAlgorithm.Argon2id, KeyDerivationAlgorithm.PBKDF2],
                [CompressionMode.None, CompressionMode.Zstd]
            )
        );

        await sut.OnNavigatedToAsync();

        Assert.Multiple(
            () => Assert.Equal(EncryptionAlgorithm.Aes, sut.SelectedEncryption.Id),
            () => Assert.Equal(KeyDerivationAlgorithm.Argon2id, sut.SelectedKeyDerivation.Id),
            () => Assert.Equal(CompressionMode.None, sut.SelectedCompression.Id)
        );
    }

    [Fact]
    internal void Constructor_WithTheRegisteredCatalog_GivesEveryOptionDistinctDisplayText()
    {
        using var provider = TestHost.CreateProvider();
        var catalog = provider.GetRequiredService<IAlgorithmCatalog>();

        SettingsViewModel sut = new(this.settingsStore, this.benchmarkService, catalog);

        List<string> names =
        [
            .. sut.EncryptionOptions.Select(static option => option.Name),
            .. sut.KeyDerivationOptions.Select(static option => option.Name),
            .. sut.CompressionOptions.Select(static option => option.Name),
        ];

        List<string> descriptions =
        [
            .. sut.EncryptionOptions.Select(static option => option.Description),
            .. sut.KeyDerivationOptions.Select(static option => option.Description),
            .. sut.CompressionOptions.Select(static option => option.Description),
        ];

        // Materialized before the assertion block because the ordering check needs to enumerate the
        // same sequence twice, once sorted and once as built.
        var encryptionIds = sut.EncryptionOptions.Select(static option => option.Id).ToList();

        Assert.Multiple(
            () => Assert.Equal(catalog.EncryptionAlgorithms.Count, sut.EncryptionOptions.Count),
            () => Assert.Equal(catalog.KeyDerivationAlgorithms.Count, sut.KeyDerivationOptions.Count),
            () => Assert.Equal(catalog.CompressionModes.Count, sut.CompressionOptions.Count),
            () => Assert.Equal(CompressionMode.None, sut.CompressionOptions[0].Id),
            () => Assert.DoesNotContain(names, string.IsNullOrWhiteSpace),
            () => Assert.DoesNotContain(descriptions, string.IsNullOrWhiteSpace),
            () => Assert.Distinct(names),
            () => Assert.Equal(encryptionIds.Order().ToList(), encryptionIds),
            () =>
                Assert.Equal<string?>(
                    [null, "en", "es"],
                    sut.LanguageOptions.Select(static option => option.Code)
                )
        );
    }

    [Fact]
    internal void Constructor_PublishesTheSettingsFilePathTheStoreResolves()
    {
        var sut = CreateSut();

        Assert.Equal("settings-path.json", sut.SettingsFilePath);
    }

    [Fact]
    internal async Task OnNavigatedToAsync_CalledAgain_DiscardsTheUnsavedEditAndShowsTheStoredSettings()
    {
        var sut = CreateSut();
        StubStoredSettings(
            new BackupCreationSettings(
                EncryptionAlgorithm.Serpent,
                KeyDerivationAlgorithm.Scrypt,
                CompressionMode.ZstdBest
            ),
            new LanguageSettings("es")
        );

        await sut.OnNavigatedToAsync();

        var (loadedEncryption, loadedKeyDerivation, loadedLanguage) = (
            sut.SelectedEncryption.Id,
            sut.SelectedKeyDerivation.Id,
            sut.SelectedLanguage.Code
        );

        sut.SelectedCompression = sut.CompressionOptions.First(static option =>
            option.Id is CompressionMode.None
        );
        var unsavedWhileEdited = sut.HasUnsavedChanges;

        await sut.OnNavigatedToAsync();

        Assert.Multiple(
            () => Assert.Equal(EncryptionAlgorithm.Serpent, loadedEncryption),
            () => Assert.Equal(KeyDerivationAlgorithm.Scrypt, loadedKeyDerivation),
            () => Assert.Equal("es", loadedLanguage),
            () => Assert.True(unsavedWhileEdited),
            () => Assert.Equal(CompressionMode.ZstdBest, sut.SelectedCompression.Id),
            () => Assert.False(sut.HasUnsavedChanges)
        );
    }

    [Fact]
    internal async Task SelectionEdit_AfterASave_HidesTheSavedNoticeAndFlagsUnsavedChanges()
    {
        var sut = CreateSut();
        _ = this
            .settingsStore.SaveAsync(
                Arg.Any<BackupCreationSettings>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        _ = this
            .settingsStore.SaveAsync(
                Arg.Any<LanguageSettings>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());

        await sut.OnNavigatedToAsync();
        await sut.SaveCommand.ExecuteAsync(null);
        var savedNoticeAfterSave = sut.ShowSavedNotice;

        sut.SelectedEncryption = sut.EncryptionOptions.First(static option =>
            option.Id is EncryptionAlgorithm.Serpent
        );

        Assert.Multiple(
            () => Assert.True(savedNoticeAfterSave),
            () => Assert.False(sut.ShowSavedNotice),
            () => Assert.True(sut.HasUnsavedChanges),
            () => Assert.True(sut.ShowUnsavedNote)
        );
    }

    [Theory]
    [InlineData("en", false)]
    [InlineData("es", true)]
    internal async Task SaveCommand_PersistsTheSelectionsAndFlagsARestartOnlyOnALanguageChange(
        string languageCode,
        bool expectRestartNote
    )
    {
        var sut = CreateSut();
        BackupCreationSettings stored = new(
            EncryptionAlgorithm.Serpent,
            KeyDerivationAlgorithm.Scrypt,
            CompressionMode.ZstdBest
        );
        StubStoredSettings(stored, new LanguageSettings("en"));

        List<BackupCreationSettings> savedDefaults = [];
        List<LanguageSettings> savedLanguages = [];
        _ = this
            .settingsStore.SaveAsync(
                Arg.Do<BackupCreationSettings>(savedDefaults.Add),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        _ = this
            .settingsStore.SaveAsync(
                Arg.Do<LanguageSettings>(savedLanguages.Add),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());

        await sut.OnNavigatedToAsync();
        sut.SelectedLanguage = sut.LanguageOptions.First(option => option.Code == languageCode);

        await sut.SaveCommand.ExecuteAsync(null);

        Assert.Multiple(
            () => Assert.Equal<BackupCreationSettings>([stored], savedDefaults),
            () => Assert.Equal<LanguageSettings>([new LanguageSettings(languageCode)], savedLanguages),
            () => Assert.True(sut.ShowSavedNotice),
            () => Assert.Equal(expectRestartNote, sut.ShowRestartNote)
        );
    }

    [Fact]
    internal async Task SaveCommand_WhenTheWriteFails_ShowsTheErrorAndSkipsTheLanguage()
    {
        var sut = CreateSut();
        _ = this
            .settingsStore.SaveAsync(
                Arg.Any<BackupCreationSettings>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Failure(MessageCode.UnexpectedErrorFormat, "settings volume is read-only")
            );

        await sut.OnNavigatedToAsync();
        await sut.SaveCommand.ExecuteAsync(null);

        Assert.Multiple(
            () => Assert.False(sut.ShowSavedNotice),
            () => Assert.False(sut.ShowRestartNote),
            () => Assert.True(sut.ShowSaveError)
        );

        _ = await this.settingsStore.DidNotReceive()
            .SaveAsync(Arg.Any<LanguageSettings>(), Arg.Any<CancellationToken>());
    }

    [SetCulture("")]
    [Theory]
    [InlineData("", 1)]
    [InlineData("   ", 1)]
    [InlineData("abc", 1)]
    [InlineData("0", 1)]
    [InlineData("-5", 1)]
    [InlineData("0.0000001", 0)]
    [InlineData("1e12", 2)]
    internal async Task RunBenchmarkCommand_WithAnUnusableAmount_ReportsItAndRunsNothing(
        string amount,
        int unitIndex
    )
    {
        var sut = CreateSut();
        var requests = StubBenchmark();

        sut.BenchmarkDataAmount = amount;
        sut.SelectedDataUnit = sut.DataUnitOptions[unitIndex];

        await sut.RunBenchmarkCommand.ExecuteAsync(null);

        Assert.Multiple(
            () => Assert.Empty(requests),
            () => Assert.True(sut.HasBenchmarkError),
            () => Assert.Equal(Strings.BenchmarkInvalidAmount, sut.BenchmarkError),
            () => Assert.False(sut.ShowBenchmarkResult),
            () => Assert.False(sut.IsBenchmarkRunning)
        );
    }

    [SetCulture("")]
    [Theory]
    [InlineData("100", 0, 104857600L)]
    [InlineData("1", 1, 1073741824L)]
    [InlineData("1.5", 1, 1610612736L)]
    [InlineData("1,5", 1, 1610612736L)]
    [InlineData("1,000", 0, 1048576000L)]
    [InlineData("1", 2, 1099511627776L)]
    internal async Task RunBenchmarkCommand_ConvertsTheAmountAndUnitIntoBytesForTheSelectedAlgorithms(
        string amount,
        int unitIndex,
        long expectedBytes
    )
    {
        var sut = CreateSut();
        StubStoredSettings(
            new BackupCreationSettings(
                EncryptionAlgorithm.Serpent,
                KeyDerivationAlgorithm.Scrypt,
                CompressionMode.ZstdBest
            ),
            LanguageSettings.DefaultValue
        );

        var requests = StubBenchmark();

        await sut.OnNavigatedToAsync();

        sut.BenchmarkDataAmount = amount;
        sut.SelectedDataUnit = sut.DataUnitOptions[unitIndex];

        await sut.RunBenchmarkCommand.ExecuteAsync(null);

        Assert.Multiple(
            () => Assert.Single(requests),
            () => Assert.Equal(expectedBytes, requests[0].DataBytes),
            () => Assert.Equal(EncryptionAlgorithm.Serpent, requests[0].EncryptionAlgorithm),
            () => Assert.Equal(KeyDerivationAlgorithm.Scrypt, requests[0].KeyDerivationAlgorithm),
            () => Assert.Equal(CompressionMode.ZstdBest, requests[0].Compression),
            () => Assert.True(sut.ShowBenchmarkResult),
            () => Assert.False(sut.HasBenchmarkError),
            () =>
                Assert.Contains(
                    DurationFormatter.Format(TimeSpan.FromMinutes(2)),
                    sut.BenchmarkDurationText,
                    StringComparison.Ordinal
                ),
            () =>
                Assert.Contains(
                    ByteSizeFormatter.Format(50_000_000),
                    sut.BenchmarkThroughputText,
                    StringComparison.Ordinal
                )
        );
    }

    [SetCulture("es-ES")]
    [Theory]
    [InlineData("1.5", 1, 1610612736L)]
    [InlineData("1,5", 1, 1610612736L)]
    [InlineData("1.000", 0, 1048576000L)]
    internal async Task RunBenchmarkCommand_InSpanish_ReadsAPointBeforeOneDigitAsTheDecimalSeparator(
        string amount,
        int unitIndex,
        long expectedBytes
    )
    {
        var sut = CreateSut();
        var requests = StubBenchmark();

        sut.BenchmarkDataAmount = amount;
        sut.SelectedDataUnit = sut.DataUnitOptions[unitIndex];

        await sut.RunBenchmarkCommand.ExecuteAsync(null);

        Assert.Multiple(
            () => Assert.Equal(expectedBytes, Assert.Single(requests).DataBytes),
            () => Assert.Contains(sut.SelectedDataUnit.Name, sut.BenchmarkDurationText, StringComparison.Ordinal)
        );
    }

    [SetCulture("")]
    [Fact]
    internal async Task RunBenchmarkCommand_WhenTheEstimateFails_ReplacesTheResultAndStaysRunnable()
    {
        var sut = CreateSut();
        _ = StubBenchmark();

        await sut.RunBenchmarkCommand.ExecuteAsync(null);
        var shownAfterTheSuccessfulRun = sut.ShowBenchmarkResult;

        _ = this
            .benchmarkService.EstimateAsync(
                Arg.Any<BenchmarkRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result<BenchmarkEstimate>.Failure(
                    MessageCode.UnexpectedErrorFormat,
                    "no strategy registered"
                )
            );

        await sut.RunBenchmarkCommand.ExecuteAsync(null);

        Assert.Multiple(
            () => Assert.True(shownAfterTheSuccessfulRun),
            () => Assert.False(sut.ShowBenchmarkResult),
            () => Assert.True(sut.HasBenchmarkError),
            () => Assert.Equal(Strings.BenchmarkFailed, sut.BenchmarkError),
            () => Assert.False(sut.IsBenchmarkRunning),
            () => Assert.True(sut.RunBenchmarkCommand.CanExecute(null))
        );
    }

    /// <summary>
    /// Builds the page over a substituted catalog covering every algorithm, with the stored settings
    /// stubbed to the built-in defaults and the settings path resolved to a known value.
    /// </summary>
    /// <returns>The system under test.</returns>
    private SettingsViewModel CreateSut()
    {
        StubStoredSettings(BackupCreationSettings.DefaultValue, LanguageSettings.DefaultValue);
        _ = this
            .settingsStore.GetFilePath<BackupCreationSettings>()
            .Returns("settings-path.json");

        return new SettingsViewModel(
            this.settingsStore,
            this.benchmarkService,
            AlgorithmCatalogs.Every()
        );
    }

    /// <summary>
    /// Stubs the persisted settings the page loads on navigation.
    /// </summary>
    /// <param name="defaults">The stored algorithm defaults.</param>
    /// <param name="language">The stored language preference.</param>
    private void StubStoredSettings(BackupCreationSettings defaults, LanguageSettings language)
    {
        _ = this
            .settingsStore.LoadAsync<BackupCreationSettings>(Arg.Any<CancellationToken>())
            .Returns(defaults);
        _ = this
            .settingsStore.LoadAsync<LanguageSettings>(Arg.Any<CancellationToken>())
            .Returns(language);
    }

    /// <summary>
    /// Makes the benchmark service return a fixed estimate and records every request it receives.
    /// </summary>
    /// <returns>The list the captured requests are appended to.</returns>
    private List<BenchmarkRequest> StubBenchmark()
    {
        List<BenchmarkRequest> requests = [];

        _ = this
            .benchmarkService.EstimateAsync(
                Arg.Do<BenchmarkRequest>(requests.Add),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result<BenchmarkEstimate>.Success(
                    new BenchmarkEstimate(
                        TimeSpan.FromMinutes(2),
                        50_000_000,
                        TimeSpan.FromMilliseconds(300),
                        1
                    )
                )
            );

        return requests;
    }
}
