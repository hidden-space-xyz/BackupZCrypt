using BackupZCrypt.Application.Commands;
using BackupZCrypt.Application.Commands.Interfaces;
using BackupZCrypt.Application.Queries;
using BackupZCrypt.Application.Queries.Interfaces;
using BackupZCrypt.Application.Utilities.Formatters;
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
    /// The substituted handler the page loads its algorithm defaults from.
    /// </summary>
    private readonly IQueryHandler<GetSettingsQuery<BackupCreationSettings>, BackupCreationSettings> creationDefaultsQuery =
        Substitute.For<IQueryHandler<GetSettingsQuery<BackupCreationSettings>, BackupCreationSettings>>();

    /// <summary>
    /// The substituted handler the page loads its language preference from.
    /// </summary>
    private readonly IQueryHandler<GetSettingsQuery<LanguageSettings>, LanguageSettings> languageQuery =
        Substitute.For<IQueryHandler<GetSettingsQuery<LanguageSettings>, LanguageSettings>>();

    /// <summary>
    /// The substituted handler the page persists its algorithm defaults through.
    /// </summary>
    private readonly ICommandHandler<SaveSettingsCommand<BackupCreationSettings>, Result> saveCreationDefaults =
        Substitute.For<ICommandHandler<SaveSettingsCommand<BackupCreationSettings>, Result>>();

    /// <summary>
    /// The substituted handler the page persists its language preference through.
    /// </summary>
    private readonly ICommandHandler<SaveSettingsCommand<LanguageSettings>, Result> saveLanguage =
        Substitute.For<ICommandHandler<SaveSettingsCommand<LanguageSettings>, Result>>();

    /// <summary>
    /// The substituted handler resolving the settings file path shown for reference.
    /// </summary>
    private readonly ISyncQueryHandler<GetSettingsFilePathQuery<BackupCreationSettings>, string> settingsFilePathQuery =
        Substitute.For<ISyncQueryHandler<GetSettingsFilePathQuery<BackupCreationSettings>, string>>();

    /// <summary>
    /// The substituted handler behind the benchmark estimate.
    /// </summary>
    private readonly IQueryHandler<EstimateBackupBenchmarkQuery, Result<BenchmarkEstimate>> estimateBenchmark =
        Substitute.For<IQueryHandler<EstimateBackupBenchmarkQuery, Result<BenchmarkEstimate>>>();

    [Fact]
    internal void Constructor_WithTheRegisteredStrategies_GivesEveryOptionDistinctDisplayText()
    {
        using var provider = TestHost.CreateProvider();
        var encryption = provider.GetServices<IEncryptionAlgorithmStrategy>().ToArray();
        var keyDerivation = provider.GetServices<IKeyDerivationAlgorithmStrategy>().ToArray();
        var compression = provider.GetServices<ICompressionStrategy>().ToArray();

        SettingsViewModel sut = new(
            this.creationDefaultsQuery,
            this.languageQuery,
            this.saveCreationDefaults,
            this.saveLanguage,
            this.settingsFilePathQuery,
            this.estimateBenchmark,
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
    internal void Constructor_PublishesTheSettingsFilePathTheHandlerResolves()
    {
        var sut = CreateSut();

        Assert.Equal("settings-path.json", sut.SettingsFilePath);
    }

    [Fact]
    internal async Task OnNavigatedToAsync_CalledAgain_KeepsTheUnsavedEditInsteadOfReloading()
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

        var (loadedEncryption, loadedKeyDerivation, loadedCompression, loadedLanguage) = (
            sut.SelectedEncryption.Id,
            sut.SelectedKeyDerivation.Id,
            sut.SelectedCompression.Id,
            sut.SelectedLanguage.Code
        );

        sut.SelectedCompression = sut.CompressionOptions.First(static option =>
            option.Id is CompressionMode.None
        );

        await sut.OnNavigatedToAsync();

        Assert.Multiple(
            () => Assert.Equal(EncryptionAlgorithm.Serpent, loadedEncryption),
            () => Assert.Equal(KeyDerivationAlgorithm.Scrypt, loadedKeyDerivation),
            () => Assert.Equal(CompressionMode.ZstdBest, loadedCompression),
            () => Assert.Equal("es", loadedLanguage),
            () => Assert.Equal(CompressionMode.None, sut.SelectedCompression.Id)
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
            .saveCreationDefaults.HandleAsync(
                Arg.Do<SaveSettingsCommand<BackupCreationSettings>>(command =>
                    savedDefaults.Add(command.Settings)
                ),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        _ = this
            .saveLanguage.HandleAsync(
                Arg.Do<SaveSettingsCommand<LanguageSettings>>(command =>
                    savedLanguages.Add(command.Settings)
                ),
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
    internal async Task SaveCommand_WhenTheWriteFails_LeavesTheSavedNoticeHiddenAndSkipsTheLanguage()
    {
        var sut = CreateSut();
        _ = this
            .saveCreationDefaults.HandleAsync(
                Arg.Any<SaveSettingsCommand<BackupCreationSettings>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Failure(MessageCode.UnexpectedErrorFormat, "settings volume is read-only")
            );

        await sut.OnNavigatedToAsync();
        await sut.SaveCommand.ExecuteAsync(null);

        Assert.Multiple(
            () => Assert.False(sut.ShowSavedNotice),
            () => Assert.False(sut.ShowRestartNote)
        );

        await this.saveLanguage.DidNotReceive()
            .HandleAsync(Arg.Any<SaveSettingsCommand<LanguageSettings>>(), Arg.Any<CancellationToken>());
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
        var queries = StubBenchmark();

        sut.BenchmarkDataAmount = amount;
        sut.SelectedDataUnit = sut.DataUnitOptions[unitIndex];

        await sut.RunBenchmarkCommand.ExecuteAsync(null);

        Assert.Multiple(
            () => Assert.Empty(queries),
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

        var queries = StubBenchmark();

        await sut.OnNavigatedToAsync();

        sut.BenchmarkDataAmount = amount;
        sut.SelectedDataUnit = sut.DataUnitOptions[unitIndex];

        await sut.RunBenchmarkCommand.ExecuteAsync(null);

        Assert.Multiple(
            () => Assert.Single(queries),
            () => Assert.Equal(expectedBytes, queries[0].DataBytes),
            () => Assert.Equal(EncryptionAlgorithm.Serpent, queries[0].EncryptionAlgorithm),
            () => Assert.Equal(KeyDerivationAlgorithm.Scrypt, queries[0].KeyDerivationAlgorithm),
            () => Assert.Equal(CompressionMode.ZstdBest, queries[0].Compression),
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

    [SetCulture("")]
    [Fact]
    internal async Task RunBenchmarkCommand_WhenTheEstimateFails_ReplacesTheResultAndStaysRunnable()
    {
        var sut = CreateSut();
        _ = StubBenchmark();

        await sut.RunBenchmarkCommand.ExecuteAsync(null);
        var shownAfterTheSuccessfulRun = sut.ShowBenchmarkResult;

        _ = this
            .estimateBenchmark.HandleAsync(
                Arg.Any<EstimateBackupBenchmarkQuery>(),
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
            .settingsFilePathQuery.Handle(Arg.Any<GetSettingsFilePathQuery<BackupCreationSettings>>())
            .Returns("settings-path.json");

        return new SettingsViewModel(
            this.creationDefaultsQuery,
            this.languageQuery,
            this.saveCreationDefaults,
            this.saveLanguage,
            this.settingsFilePathQuery,
            this.estimateBenchmark,
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
            .creationDefaultsQuery.HandleAsync(
                Arg.Any<GetSettingsQuery<BackupCreationSettings>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(defaults);
        _ = this
            .languageQuery.HandleAsync(
                Arg.Any<GetSettingsQuery<LanguageSettings>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(language);
    }

    /// <summary>
    /// Makes the benchmark handler return a fixed estimate and records every query it receives.
    /// </summary>
    /// <returns>The list the captured queries are appended to.</returns>
    private List<EstimateBackupBenchmarkQuery> StubBenchmark()
    {
        List<EstimateBackupBenchmarkQuery> queries = [];

        _ = this
            .estimateBenchmark.HandleAsync(
                Arg.Do<EstimateBackupBenchmarkQuery>(queries.Add),
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

        return queries;
    }
}
