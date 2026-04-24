namespace BackupZCrypt.Test.Application.Services;

using BackupZCrypt.Application.Services;
using BackupZCrypt.Application.ValueObjects.Backup;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Infrastructure.Services.FileSystem;

[TestFixture]
internal sealed class BackupCreationSettingsServiceTests
{
    [Test]
    public async Task GetOrCreateAsync_WhenFileDoesNotExist_CreatesDefaultSettingsFile()
    {
        var tempDirectoryPath = Path.Combine(
            Path.GetTempPath(),
            $"bzc-settings-{Guid.NewGuid():N}");
        var settingsFilePath = Path.Combine(tempDirectoryPath, "settings.json");
        BackupCreationSettingsService service = new(new FileOperationsService(), settingsFilePath);

        try
        {
            var settings = await service.GetOrCreateAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(settings, Is.EqualTo(BackupCreationSettings.Default));
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
    public async Task SaveAsync_WhenSettingsChange_PersistsRoundTrip()
    {
        var tempDirectoryPath = Path.Combine(
            Path.GetTempPath(),
            $"bzc-settings-{Guid.NewGuid():N}");
        var settingsFilePath = Path.Combine(tempDirectoryPath, "settings.json");
        BackupCreationSettingsService service = new(new FileOperationsService(), settingsFilePath);
        BackupCreationSettings expectedSettings = new(
            false,
            EncryptionAlgorithm.Serpent,
            KeyDerivationAlgorithm.Scrypt,
            NameObfuscationMode.Sha512,
            CompressionMode.ZstdBest);

        try
        {
            await service.SaveAsync(expectedSettings);

            var actualSettings = await service.GetOrCreateAsync();

            Assert.That(actualSettings, Is.EqualTo(expectedSettings));
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
        BackupCreationSettingsService service = new(new FileOperationsService());

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
            $"bzc-settings-{Guid.NewGuid():N}");
        var settingsFilePath = Path.Combine(tempDirectoryPath, "settings.json");
        Directory.CreateDirectory(tempDirectoryPath);
        File.WriteAllText(settingsFilePath, "{ invalid json }");
        BackupCreationSettingsService service = new(new FileOperationsService(), settingsFilePath);

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
