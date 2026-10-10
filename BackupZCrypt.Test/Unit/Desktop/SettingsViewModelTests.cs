using BackupZCrypt.Application.Common;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Application.ValueObjects.Benchmark;
using BackupZCrypt.Application.ValueObjects.Settings;
using BackupZCrypt.Desktop.Resources;
using BackupZCrypt.Desktop.ViewModels;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Localization;
using BackupZCrypt.Test.Common;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

namespace BackupZCrypt.Test.Unit.Desktop;

/// <summary>
/// Unit tests for the settings page: the option lists it builds from the registered strategies, the
/// load-once behaviour of the stored defaults, persistence and the language restart note, and the
/// benchmark amount parsing.
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
    internal void Constructor_WithTheRegisteredStrategies_GivesEveryOptionDistinctDisplayText()
    {
        using var provider = TestHost.CreateProvider();
        var encryption = provider.GetServices<IEncryptionAlgorithmStrategy>().ToArray();
        var keyDerivation = provider.GetServices<IKeyDerivationAlgorithmStrategy>().ToArray();
        var compression = provider.GetServices<ICompressionStrategy>().ToArray();

        SettingsViewModel sut = new(
            this.settingsStore,
            this.benchmarkService,
            encryption,
            keyDerivation,
            compression
        );

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
            () => Assert.Equal(encryption.Length, sut.EncryptionOptions.Count),
            () => Assert.Equal(keyDerivation.Length, sut.KeyDerivationOptions.Count),
            () => Assert.Equal(compression.Length + 1, sut.CompressionOptions.Count),
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
    /// Builds one substituted strategy per encryption algorithm so every algorithm is selectable.
    /// </summary>
    /// <returns>The substituted encryption strategies.</returns>
    private static List<IEncryptionAlgorithmStrategy> EncryptionStrategies()
    {
        List<IEncryptionAlgorithmStrategy> strategies = [];

        foreach (var id in Enum.GetValues<EncryptionAlgorithm>())
        {
            var strategy = Substitute.For<IEncryptionAlgorithmStrategy>();
            _ = strategy.Id.Returns(id);
            strategies.Add(strategy);
        }

        return strategies;
    }

    /// <summary>
    /// Builds one substituted strategy per key-derivation algorithm.
    /// </summary>
    /// <returns>The substituted key-derivation strategies.</returns>
    private static List<IKeyDerivationAlgorithmStrategy> KeyDerivationStrategies()
    {
        List<IKeyDerivationAlgorithmStrategy> strategies = [];

        foreach (var id in Enum.GetValues<KeyDerivationAlgorithm>())
        {
            var strategy = Substitute.For<IKeyDerivationAlgorithmStrategy>();
            _ = strategy.Id.Returns(id);
            strategies.Add(strategy);
        }

        return strategies;
    }

    /// <summary>
    /// Builds one substituted strategy per compression mode except <see cref="CompressionMode.None"/>,
    /// which the page adds itself.
    /// </summary>
    /// <returns>The substituted compression strategies.</returns>
    private static List<ICompressionStrategy> CompressionStrategies()
    {
        List<ICompressionStrategy> strategies = [];

        foreach (var id in Enum.GetValues<CompressionMode>())
        {
            if (id is CompressionMode.None)
            {
                continue;
            }

            var strategy = Substitute.For<ICompressionStrategy>();
            _ = strategy.Id.Returns(id);
            strategies.Add(strategy);
        }

        return strategies;
    }

    /// <summary>
    /// Builds the page over substituted strategies covering every algorithm, with the stored settings
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
            EncryptionStrategies(),
            KeyDerivationStrategies(),
            CompressionStrategies()
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
