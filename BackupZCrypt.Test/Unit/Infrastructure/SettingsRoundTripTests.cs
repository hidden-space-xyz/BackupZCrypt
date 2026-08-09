using BackupZCrypt.Application.ValueObjects.Settings;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Infrastructure.Services;
using BackupZCrypt.Infrastructure.Services.Settings;
using BackupZCrypt.Test.Common;

namespace BackupZCrypt.Test.Unit.Infrastructure;

/// <summary>
/// Tests that every persisted settings type survives a save and reload through the settings service,
/// and that the types are backed by separate files inside the configured base directory.
/// </summary>
/// <remarks>
/// The settings records hold no logic of their own, so they are exercised where they carry real
/// behavior: JSON persistence. Each test drives the service with the real file operations
/// implementation against its own temporary directory, never the per-user application data folder.
/// </remarks>
public sealed class SettingsRoundTripTests
{
    /// <summary>
    /// A stand-in for a remembered source path. Nothing is created on disk because the settings
    /// records simply store whatever text the user last selected.
    /// </summary>
    private static readonly string SourcePath = Path.Combine(
        Path.GetTempPath(),
        "bzc-recent-source"
    );

    /// <summary>
    /// A stand-in for a remembered destination path, kept distinct from <see cref="SourcePath"/> so a
    /// reload that mixed the two members up would be visible.
    /// </summary>
    private static readonly string DestinationPath = Path.Combine(
        Path.GetTempPath(),
        "bzc-recent-destination"
    );

    /// <summary>
    /// A deliberately short remembered path: written over <see cref="SourcePath"/> it shrinks the JSON
    /// payload, so a write that failed to truncate would leave the tail of the previous file behind.
    /// </summary>
    private const string ShortPath = "s";

    /// <summary>
    /// The half-populated recent-path shapes. Every member of the record is nullable with a null
    /// default, so each one has to round-trip as null without dragging its populated sibling back to
    /// the default value with it.
    /// </summary>
    /// <returns>Recent path settings with exactly one member populated.</returns>
    public static TheoryData<RecentPathSettings> PartiallyPopulatedRecentPaths()
    {
        return new()
        {
            new RecentPathSettings(SourcePath, null),
            new RecentPathSettings(null, DestinationPath),
        };
    }

    [Fact]
    internal void GetFilePath_EverySettingsType_ResolvesToADistinctFileInsideTheBaseDirectory()
    {
        using var dir = new TempDir();
        var service = new SettingsService(new FileOperationsService(), dir.Path);

        string[] paths =
        [
            service.GetFilePath<BackupCreationSettings>(),
            service.GetFilePath<LanguageSettings>(),
            service.GetFilePath<RecentPathSettings>(),
        ];

        Assert.Multiple(
            () => Assert.Distinct(paths),
            () =>
                Assert.All(
                    paths.Select(Path.GetDirectoryName),
                    directory => Assert.Equal(Path.GetFullPath(dir.Path), directory)
                )
        );
    }

    [Fact]
    internal async Task SaveThenGetOrCreate_EverySettingsType_RoundtripsWithoutClobberingTheOthers()
    {
        using var dir = new TempDir();
        var service = new SettingsService(new FileOperationsService(), dir.Path);

        var creation = new BackupCreationSettings(
            EncryptionAlgorithm.ChaCha20,
            KeyDerivationAlgorithm.PBKDF2,
            CompressionMode.ZstdFast
        );
        var language = new LanguageSettings("es");
        var recentPaths = new RecentPathSettings(SourcePath, DestinationPath);

        await service.SaveAsync(creation, TestContext.Current.CancellationToken);
        await service.SaveAsync(language, TestContext.Current.CancellationToken);
        await service.SaveAsync(recentPaths, TestContext.Current.CancellationToken);

        var loadedCreation = await service.GetOrCreateAsync<BackupCreationSettings>(
            TestContext.Current.CancellationToken
        );
        var loadedLanguage = await service.GetOrCreateAsync<LanguageSettings>(
            TestContext.Current.CancellationToken
        );
        var loadedRecentPaths = await service.GetOrCreateAsync<RecentPathSettings>(
            TestContext.Current.CancellationToken
        );

        Assert.Multiple(
            () => Assert.Equal(creation, loadedCreation),
            () => Assert.Equal(language, loadedLanguage),
            () => Assert.Equal(recentPaths, loadedRecentPaths)
        );
    }

    [Theory]
    [MemberData(nameof(PartiallyPopulatedRecentPaths))]
    internal async Task SaveThenGetOrCreate_RecentPathsWithOneUnsetMember_PreservesNullAndTheSetMember(
        RecentPathSettings settings
    )
    {
        using var dir = new TempDir();
        var service = new SettingsService(new FileOperationsService(), dir.Path);

        await service.SaveAsync(settings, TestContext.Current.CancellationToken);
        var loaded = await service.GetOrCreateAsync<RecentPathSettings>(
            TestContext.Current.CancellationToken
        );

        Assert.Equal(settings, loaded);
    }

    [Fact]
    internal async Task SaveAsync_OverwritingWithAShorterValue_ReplacesTheWholeFile()
    {
        using var dir = new TempDir();
        var service = new SettingsService(new FileOperationsService(), dir.Path);

        await service.SaveAsync(
            new RecentPathSettings(SourcePath, DestinationPath),
            TestContext.Current.CancellationToken
        );

        var trimmed = new RecentPathSettings(ShortPath);
        await service.SaveAsync(trimmed, TestContext.Current.CancellationToken);

        var loaded = await service.GetOrCreateAsync<RecentPathSettings>(
            TestContext.Current.CancellationToken
        );

        Assert.Equal(trimmed, loaded);
    }
}
