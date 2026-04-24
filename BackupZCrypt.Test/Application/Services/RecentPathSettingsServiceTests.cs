namespace BackupZCrypt.Test.Application.Services;

using BackupZCrypt.Application.Services;
using BackupZCrypt.Application.ValueObjects.Backup;
using BackupZCrypt.Infrastructure.Services.FileSystem;

[TestFixture]
internal sealed class RecentPathSettingsServiceTests
{
    [Test]
    public async Task GetOrCreateAsync_WhenFileDoesNotExist_CreatesDefaultSettingsFile()
    {
        var tempDirectoryPath = Path.Combine(
            Path.GetTempPath(),
            $"bzc-recent-paths-{Guid.NewGuid():N}");
        var settingsFilePath = Path.Combine(tempDirectoryPath, "settings.json");
        RecentPathSettingsService service = new(new FileOperationsService(), settingsFilePath);

        try
        {
            var settings = await service.GetOrCreateAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(settings, Is.EqualTo(RecentPathSettings.Default));
                Assert.That(File.Exists(settingsFilePath), Is.True);
            }
        }
        finally
        {
            if (Directory.Exists(tempDirectoryPath))
            {
                Directory.Delete(tempDirectoryPath, true);
            }
        }
    }

    [Test]
    public async Task RememberPathsAsync_WhenPathsChange_PersistsRoundTrip()
    {
        var tempDirectoryPath = Path.Combine(
            Path.GetTempPath(),
            $"bzc-recent-paths-{Guid.NewGuid():N}");
        var settingsFilePath = Path.Combine(tempDirectoryPath, "settings.json");
        RecentPathSettingsService service = new(new FileOperationsService(), settingsFilePath);
        var expectedSourcePath = Path.Combine(tempDirectoryPath, "source");
        var expectedDestinationPath = Path.Combine(tempDirectoryPath, "destination");

        try
        {
            await service.RememberPathsAsync(expectedSourcePath, expectedDestinationPath);

            var actualSettings = await service.GetOrCreateAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(actualSettings.LastSourcePath, Is.EqualTo(Path.GetFullPath(expectedSourcePath)));
                Assert.That(actualSettings.LastDestinationPath, Is.EqualTo(Path.GetFullPath(expectedDestinationPath)));
            }
        }
        finally
        {
            if (Directory.Exists(tempDirectoryPath))
            {
                Directory.Delete(tempDirectoryPath, true);
            }
        }
    }

    [Test]
    public void Constructor_WithoutCustomPath_UsesSystemTemporaryDirectory()
    {
        RecentPathSettingsService service = new(new FileOperationsService());

        var relativePath = Path.GetRelativePath(
            Path.GetFullPath(Path.GetTempPath()),
            service.SettingsFilePath);

        Assert.That(relativePath.StartsWith("..", StringComparison.Ordinal), Is.False);
    }

    [Test]
    public void GetOrCreateAsync_WhenJsonIsInvalid_ThrowsInvalidOperationException()
    {
        var tempDirectoryPath = Path.Combine(
            Path.GetTempPath(),
            $"bzc-recent-paths-{Guid.NewGuid():N}");
        var settingsFilePath = Path.Combine(tempDirectoryPath, "settings.json");
        Directory.CreateDirectory(tempDirectoryPath);
        File.WriteAllText(settingsFilePath, "{ invalid json }");
        RecentPathSettingsService service = new(new FileOperationsService(), settingsFilePath);

        try
        {
            Assert.That(
                async () => await service.GetOrCreateAsync(),
                Throws.TypeOf<InvalidOperationException>());
        }
        finally
        {
            Directory.Delete(tempDirectoryPath, true);
        }
    }
}
