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

    [Fact]
    internal async Task GetOrCreateAsync_WhenNoFile_ReturnsDefaultsAndCreatesFile()
    {
        using var dir = new TempDir();
        var service = new SettingsService(new FileOperationsService(), dir.Path);

        var filePath = service.GetFilePath<BackupCreationSettings>();
        Assert.False(File.Exists(filePath));

        var settings = await service.GetOrCreateAsync<BackupCreationSettings>(
            TestContext.Current.CancellationToken
        );

        Assert.Multiple(
            () => Assert.Equal(BackupCreationSettings.DefaultValue, settings),
            () => Assert.True(File.Exists(filePath), "GetOrCreateAsync did not persist the defaults file.")
        );
    }

    [Fact]
    internal async Task SaveThenGetOrCreate_RoundtripsNonDefaultValue()
    {
        using var dir = new TempDir();
        var service = new SettingsService(new FileOperationsService(), dir.Path);

        var custom = new BackupCreationSettings(
            EncryptionAlgorithm.ChaCha20,
            KeyDerivationAlgorithm.Scrypt,
            CompressionMode.ZstdBest
        );
        Assert.NotEqual(BackupCreationSettings.DefaultValue, custom);

        await service.SaveAsync(custom, TestContext.Current.CancellationToken);
        var loaded = await service.GetOrCreateAsync<BackupCreationSettings>(
            TestContext.Current.CancellationToken
        );

        Assert.Equal(custom, loaded);
    }

    [Fact]
    internal async Task GetOrCreateAsync_WhenFileCorrupted_SelfHealsToDefaults()
    {
        using var dir = new TempDir();
        var service = new SettingsService(new FileOperationsService(), dir.Path);

        var filePath = service.GetFilePath<BackupCreationSettings>();
        _ = Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        await File.WriteAllTextAsync(
            filePath,
            "this is not valid json {{{",
            TestContext.Current.CancellationToken
        );

        var settings = await service.GetOrCreateAsync<BackupCreationSettings>(
            TestContext.Current.CancellationToken
        );

        Assert.Equal(BackupCreationSettings.DefaultValue, settings);

        var reread = await service.GetOrCreateAsync<BackupCreationSettings>(
            TestContext.Current.CancellationToken
        );
        Assert.Equal(BackupCreationSettings.DefaultValue, reread);
    }

    [Fact]
    internal async Task GetOrCreateAsync_WhenFileExceedsSafetyLimit_ReplacesItWithDefaults()
    {
        using var dir = new TempDir();
        var service = new SettingsService(new FileOperationsService(), dir.Path);
        var filePath = service.GetFilePath<BackupCreationSettings>();
        _ = Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        await File.WriteAllBytesAsync(
            filePath,
            new byte[(1024 * 1024) + 1],
            TestContext.Current.CancellationToken
        );

        var settings = await service.GetOrCreateAsync<BackupCreationSettings>(
            TestContext.Current.CancellationToken
        );

        Assert.Multiple(
            () => Assert.Equal(BackupCreationSettings.DefaultValue, settings),
            () => Assert.True(new FileInfo(filePath).Length < 1024 * 1024)
        );
    }

    [Theory]
    [InlineData("null")]
    [InlineData("")]
    internal async Task GetOrCreateAsync_WhenFileHoldsNoUsableSettings_SelfHealsAndRewritesTheFile(
        string fileContent
    )
    {
        using var dir = new TempDir();
        var service = new SettingsService(new FileOperationsService(), dir.Path);

        var filePath = service.GetFilePath<BackupCreationSettings>();
        _ = Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        await File.WriteAllTextAsync(filePath, fileContent, TestContext.Current.CancellationToken);

        var settings = await service.GetOrCreateAsync<BackupCreationSettings>(
            TestContext.Current.CancellationToken
        );

        Assert.Equal(BackupCreationSettings.DefaultValue, settings);

        var onDisk = await File.ReadAllTextAsync(filePath, TestContext.Current.CancellationToken);
        Assert.Multiple(
            () => Assert.NotEqual(fileContent, onDisk),
            () => Assert.Equal(
                BackupCreationSettings.DefaultValue,
                JsonSerializer.Deserialize<BackupCreationSettings>(onDisk, SettingsFileOptions)
            )
        );
    }

    [Fact]
    internal async Task GetOrCreateAsync_WhenBaseDirectoryMissing_CreatesTheTreeAndPersistsDefaults()
    {
        using var dir = new TempDir();
        var baseDirectory = Path.Combine(dir.Path, "BackupZCrypt", "settings");
        var service = new SettingsService(new FileOperationsService(), baseDirectory);
        Assert.False(Directory.Exists(baseDirectory));

        var settings = await service.GetOrCreateAsync<LanguageSettings>(
            TestContext.Current.CancellationToken
        );

        Assert.Multiple(
            () => Assert.Equal(LanguageSettings.DefaultValue, settings),
            () => Assert.True(
                File.Exists(service.GetFilePath<LanguageSettings>()),
                "A first run has to create the missing settings directory tree, not fail to save."
            )
        );
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    internal async Task SaveAsync_WhenThePathHasNoDirectory_ThrowsWithoutWritingAnything(
        string? directoryName
    )
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "bzc-settings-without-directory");

        var fileOperations = Substitute.For<IFileOperationsService>();
        _ = fileOperations.GetDirectoryName(Arg.Any<string>()).Returns(directoryName);

        var service = new SettingsService(fileOperations, baseDirectory);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SaveAsync(
                BackupCreationSettings.DefaultValue,
                TestContext.Current.CancellationToken
            )
        );

        Assert.Contains(
            service.GetFilePath<BackupCreationSettings>(),
            exception.Message,
            StringComparison.Ordinal
        );

        await fileOperations.DidNotReceive()
            .CreateDirectoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await fileOperations.DidNotReceive()
            .WriteFileAtomicallyAsync(
                Arg.Any<string>(),
                Arg.Any<Func<Stream, CancellationToken, Task>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    internal void GetFilePath_WithNoBaseDirectoryOverride_ResolvesUnderTheUserApplicationDataFolder()
    {
        var expectedDirectory = Path.GetFullPath(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BackupZCrypt"
            )
        );

        var service = new SettingsService(Substitute.For<IFileOperationsService>());

        var filePath = service.GetFilePath<BackupCreationSettings>();

        Assert.Multiple(
            () => Assert.Equal(expectedDirectory, Path.GetDirectoryName(filePath)),
            () => Assert.Equal(BackupCreationSettings.FileName, Path.GetFileName(filePath))
        );
    }
}
