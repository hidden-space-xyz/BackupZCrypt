using System.Text.Json;
using System.Text.Json.Serialization;

using BackupZCrypt.Application.ValueObjects.Settings;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Services.Interfaces;
using BackupZCrypt.Infrastructure.Services;
using BackupZCrypt.Infrastructure.Services.Settings;
using BackupZCrypt.Test.Common;

using NSubstitute;

namespace BackupZCrypt.Test.Integration;

/// <summary>
/// Integration tests for loading, creating, and persisting settings through the settings service.
/// </summary>
/// <remarks>
/// Every case works against a temporary base directory, and the one case that resolves the real user
/// profile path substitutes the file service, so the fixture never reads or writes the settings of the
/// machine it runs on.
/// </remarks>
public sealed class SettingsServiceTests
{
    /// <summary>
    /// Mirrors the options the settings service writes with, so a file read back by a test is parsed
    /// exactly the way the production reader parses it, enum names included.
    /// </summary>
    private static readonly JsonSerializerOptions SettingsFileOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Test]
    public async Task GetOrCreateAsync_WhenNoFile_ReturnsDefaultsAndCreatesFile()
    {
        using var dir = new TempDir();
        var service = new SettingsService(new FileOperationsService(), dir.Path);

        var filePath = service.GetFilePath<BackupCreationSettings>();
        Assert.That(File.Exists(filePath), Is.False);

        var settings = await service.GetOrCreateAsync<BackupCreationSettings>();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(settings, Is.EqualTo(BackupCreationSettings.DefaultValue));
            Assert.That(File.Exists(filePath), Is.True, "GetOrCreateAsync did not persist the defaults file.");
        }
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
        _ = Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        await File.WriteAllTextAsync(filePath, "this is not valid json {{{");

        var settings = await service.GetOrCreateAsync<BackupCreationSettings>();

        Assert.That(settings, Is.EqualTo(BackupCreationSettings.DefaultValue));

        var reread = await service.GetOrCreateAsync<BackupCreationSettings>();
        Assert.That(reread, Is.EqualTo(BackupCreationSettings.DefaultValue));
    }

    [TestCase("null", TestName = "GetOrCreateAsync_WhenFileHoldsJsonNull_SelfHealsAndRewritesTheFile")]
    [TestCase("", TestName = "GetOrCreateAsync_WhenFileIsEmpty_SelfHealsAndRewritesTheFile")]
    public async Task GetOrCreateAsync_WhenFileHoldsNoUsableSettings_SelfHealsAndRewritesTheFile(
        string fileContent
    )
    {
        using var dir = new TempDir();
        var service = new SettingsService(new FileOperationsService(), dir.Path);

        var filePath = service.GetFilePath<BackupCreationSettings>();
        _ = Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        await File.WriteAllTextAsync(filePath, fileContent);

        var settings = await service.GetOrCreateAsync<BackupCreationSettings>();

        Assert.That(settings, Is.EqualTo(BackupCreationSettings.DefaultValue));

        var onDisk = await File.ReadAllTextAsync(filePath);
        Assert.That(
            onDisk,
            Is.Not.EqualTo(fileContent),
            "The unusable settings file was returned as defaults but never replaced on disk."
        );
        Assert.That(
            JsonSerializer.Deserialize<BackupCreationSettings>(onDisk, SettingsFileOptions),
            Is.EqualTo(BackupCreationSettings.DefaultValue)
        );
    }

    [Test]
    public async Task GetOrCreateAsync_WhenBaseDirectoryMissing_CreatesTheTreeAndPersistsDefaults()
    {
        using var dir = new TempDir();
        var baseDirectory = Path.Combine(dir.Path, "BackupZCrypt", "settings");
        var service = new SettingsService(new FileOperationsService(), baseDirectory);
        Assert.That(Directory.Exists(baseDirectory), Is.False);

        var settings = await service.GetOrCreateAsync<LanguageSettings>();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(settings, Is.EqualTo(LanguageSettings.DefaultValue));
            Assert.That(
                File.Exists(service.GetFilePath<LanguageSettings>()),
                Is.True,
                "A first run has to create the missing settings directory tree, not fail to save."
            );
        }
    }

    [TestCase(null)]
    [TestCase("   ")]
    public async Task SaveAsync_WhenThePathHasNoDirectory_ThrowsWithoutWritingAnything(
        string? directoryName
    )
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "bzc-settings-without-directory");

        var fileOperations = Substitute.For<IFileOperationsService>();
        _ = fileOperations.GetDirectoryName(Arg.Any<string>()).Returns(directoryName);

        var service = new SettingsService(fileOperations, baseDirectory);

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SaveAsync(BackupCreationSettings.DefaultValue)
        );

        Assert.That(
            exception?.Message,
            Does.Contain(service.GetFilePath<BackupCreationSettings>()),
            "Creating a directory from a blank name would resolve against the process working directory, so "
                + "the save has to fail loudly and name the path instead of writing the file somewhere the "
                + "next run will never look for it."
        );

        await fileOperations.DidNotReceive()
            .CreateDirectoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await fileOperations.DidNotReceive()
            .WriteAllBytesAsync(
                Arg.Any<string>(),
                Arg.Any<byte[]>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Test]
    public void GetFilePath_WithNoBaseDirectoryOverride_ResolvesUnderTheUserApplicationDataFolder()
    {
        var expectedDirectory = Path.GetFullPath(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BackupZCrypt"
            )
        );

        var service = new SettingsService(Substitute.For<IFileOperationsService>());

        var filePath = service.GetFilePath<BackupCreationSettings>();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                Path.GetDirectoryName(filePath),
                Is.EqualTo(expectedDirectory),
                "Settings have to survive a reinstall of the app in a new folder, so they belong to the "
                    + "user's profile rather than to whatever directory the process happens to start in."
            );
            Assert.That(Path.GetFileName(filePath), Is.EqualTo(BackupCreationSettings.FileName));
        }
    }
}
