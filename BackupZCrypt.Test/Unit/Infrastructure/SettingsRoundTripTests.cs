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
    private static IEnumerable<TestCaseData> PartiallyPopulatedRecentPaths()
    {
        return
        [
            new TestCaseData(new RecentPathSettings(SourcePath, null)).SetName(
                "SaveThenGetOrCreate_RecentPathsWithUnsetDestination_PreservesNullAndTheSetMember"
            ),
            new TestCaseData(new RecentPathSettings(null, DestinationPath)).SetName(
                "SaveThenGetOrCreate_RecentPathsWithUnsetSource_PreservesNullAndTheSetMember"
            ),
        ];
    }

    [Test]
    public void GetFilePath_EverySettingsType_ResolvesToADistinctFileInsideTheBaseDirectory()
    {
        using var dir = new TempDir();
        var service = new SettingsService(new FileOperationsService(), dir.Path);

        string[] paths =
        [
            service.GetFilePath<BackupCreationSettings>(),
            service.GetFilePath<LanguageSettings>(),
            service.GetFilePath<RecentPathSettings>(),
        ];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                paths,
                Is.Unique,
                "Two settings types share a file name and would overwrite each other."
            );
            Assert.That(
                paths.Select(Path.GetDirectoryName),
                Is.All.EqualTo(Path.GetFullPath(dir.Path)),
                "A settings file resolved outside the configured base directory."
            );
        }
    }

    [Test]
    public async Task SaveThenGetOrCreate_EverySettingsType_RoundtripsWithoutClobberingTheOthers()
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

        await service.SaveAsync(creation);
        await service.SaveAsync(language);
        await service.SaveAsync(recentPaths);

        var loadedCreation = await service.GetOrCreateAsync<BackupCreationSettings>();
        var loadedLanguage = await service.GetOrCreateAsync<LanguageSettings>();
        var loadedRecentPaths = await service.GetOrCreateAsync<RecentPathSettings>();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(loadedCreation, Is.EqualTo(creation));
            Assert.That(loadedLanguage, Is.EqualTo(language));
            Assert.That(loadedRecentPaths, Is.EqualTo(recentPaths));
        }
    }

    [TestCaseSource(nameof(PartiallyPopulatedRecentPaths))]
    public async Task SaveThenGetOrCreate_RecentPathsWithOneUnsetMember_PreservesNullAndTheSetMember(
        RecentPathSettings settings
    )
    {
        using var dir = new TempDir();
        var service = new SettingsService(new FileOperationsService(), dir.Path);

        await service.SaveAsync(settings);
        var loaded = await service.GetOrCreateAsync<RecentPathSettings>();

        Assert.That(
            loaded,
            Is.EqualTo(settings),
            "A null member must round-trip as null without resetting the populated member."
        );
    }

    [Test]
    public async Task SaveAsync_OverwritingWithAShorterValue_ReplacesTheWholeFile()
    {
        using var dir = new TempDir();
        var service = new SettingsService(new FileOperationsService(), dir.Path);

        await service.SaveAsync(new RecentPathSettings(SourcePath, DestinationPath));

        var trimmed = new RecentPathSettings(ShortPath);
        await service.SaveAsync(trimmed);

        var loaded = await service.GetOrCreateAsync<RecentPathSettings>();

        Assert.That(
            loaded,
            Is.EqualTo(trimmed),
            "The shorter payload left remnants of the previous file, so the reload fell back to defaults."
        );
    }
}
