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
    [Fact]
    public async Task GetOrCreateAsync_WhenNoFile_ReturnsDefaultsAndCreatesFile()
    {
        using var dir = new TempDir();
        var service = new SettingsService(new FileOperationsService(), dir.Path);

        var filePath = service.GetFilePath<BackupCreationSettings>();
        Assert.False(File.Exists(filePath));

        var settings = await service.GetOrCreateAsync<BackupCreationSettings>();

        Assert.Equal(BackupCreationSettings.DefaultValue, settings);
        Assert.True(File.Exists(filePath), "GetOrCreateAsync did not persist the defaults file.");
    }

    [Fact]
    public async Task SaveThenGetOrCreate_RoundtripsNonDefaultValue()
    {
        using var dir = new TempDir();
        var service = new SettingsService(new FileOperationsService(), dir.Path);

        var custom = new BackupCreationSettings(
            EncryptionAlgorithm.ChaCha20,
            KeyDerivationAlgorithm.Scrypt,
            CompressionMode.ZstdBest
        );
        Assert.NotEqual(BackupCreationSettings.DefaultValue, custom);

        await service.SaveAsync(custom);
        var loaded = await service.GetOrCreateAsync<BackupCreationSettings>();

        Assert.Equal(custom, loaded);
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenFileCorrupted_SelfHealsToDefaults()
    {
        using var dir = new TempDir();
        var service = new SettingsService(new FileOperationsService(), dir.Path);

        var filePath = service.GetFilePath<BackupCreationSettings>();
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        await File.WriteAllTextAsync(filePath, "this is not valid json {{{");

        var settings = await service.GetOrCreateAsync<BackupCreationSettings>();

        Assert.Equal(BackupCreationSettings.DefaultValue, settings);

        // Self-healing rewrote the file with valid defaults, so a second read also succeeds.
        var reread = await service.GetOrCreateAsync<BackupCreationSettings>();
        Assert.Equal(BackupCreationSettings.DefaultValue, reread);
    }
}
