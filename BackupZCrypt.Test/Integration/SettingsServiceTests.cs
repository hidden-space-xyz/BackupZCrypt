using BackupZCrypt.Application.Services;
using BackupZCrypt.Application.ValueObjects.Backup;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Infrastructure.Services;
using BackupZCrypt.Test.Common;

namespace BackupZCrypt.Test.Integration;

// SettingsService persisted against the real file system, rooted at a throwaway temp
// directory (baseDirectoryPath) so nothing touches the user's LocalApplicationData.
public sealed class SettingsServiceTests
{
    [Test]
    public async Task GetOrCreateAsync_WhenNoFile_ReturnsDefaultsAndCreatesFile()
    {
        using var dir = new TempDir();
        var service = new SettingsService(new FileOperationsService(), dir.Path);

        var filePath = service.GetFilePath<BackupCreationSettings>();
        Assert.That(File.Exists(filePath), Is.False);

        var settings = await service.GetOrCreateAsync<BackupCreationSettings>();

        Assert.That(settings, Is.EqualTo(BackupCreationSettings.DefaultValue));
        Assert.That(File.Exists(filePath), Is.True, "GetOrCreateAsync did not persist the defaults file.");
    }

    [Test]
    public async Task SaveThenGetOrCreate_RoundtripsNonDefaultValue()
    {
        using var dir = new TempDir();
        var service = new SettingsService(new FileOperationsService(), dir.Path);

        var custom = new BackupCreationSettings(
            EncryptionAlgorithm.ChaCha20,
            KeyDerivationAlgorithm.Scrypt,
            CompressionMode.ZstdBest
        );
        Assert.That(custom, Is.Not.EqualTo(BackupCreationSettings.DefaultValue));

        await service.SaveAsync(custom);
        var loaded = await service.GetOrCreateAsync<BackupCreationSettings>();

        Assert.That(loaded, Is.EqualTo(custom));
    }

    [Test]
    public async Task GetOrCreateAsync_WhenFileCorrupted_SelfHealsToDefaults()
    {
        using var dir = new TempDir();
        var service = new SettingsService(new FileOperationsService(), dir.Path);

        var filePath = service.GetFilePath<BackupCreationSettings>();
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        await File.WriteAllTextAsync(filePath, "this is not valid json {{{");

        var settings = await service.GetOrCreateAsync<BackupCreationSettings>();

        Assert.That(settings, Is.EqualTo(BackupCreationSettings.DefaultValue));

        // Self-healing rewrote the file with valid defaults, so a second read also succeeds.
        var reread = await service.GetOrCreateAsync<BackupCreationSettings>();
        Assert.That(reread, Is.EqualTo(BackupCreationSettings.DefaultValue));
    }
}
