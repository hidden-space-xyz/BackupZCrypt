using BackupZCrypt.Application.Common;
using BackupZCrypt.Application.Engine;
using BackupZCrypt.Domain.Archive;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Test.Common;

using Microsoft.Extensions.DependencyInjection;

namespace BackupZCrypt.Test.Integration;

/// <summary>
/// Integration tests for the previews the engine runs before an update or restore, against a real
/// archive: what they report about the backup, and that a wrong password is refused before anything
/// would be written.
/// </summary>
public sealed class BackupPreviewTests
{
    /// <summary>
    /// The password every archive in these tests is created with.
    /// </summary>
    private const string Password = "Correct-Horse-Battery-Staple-42";

    [Fact]
    internal async Task PreviewRestoreAsync_OpenedArchive_ReportsEveryRecordedFileAndItsSize()
    {
        await using var provider = TestHost.CreateProvider();
        var service = provider.GetRequiredService<IChunkedBackupService>();
        using var source = new TempDir();
        using var archive = new TempDir();
        _ = source.WriteText("a.txt", "alpha");
        _ = source.WriteText(Path.Join("dir", "b.txt"), "bravo!");
        await CreateArchiveAsync(service, source.Path, archive.Path);

        var preview = await service.PreviewRestoreAsync(
            archive.Path,
            Password,
            TestContext.Current.CancellationToken
        );

        Assert.Multiple(
            () => Assert.True(preview.IsSuccess),
            () => Assert.Equal(2, preview.Value.FileCount),
            () => Assert.Equal(11, preview.Value.TotalBytes),
            () => Assert.Empty(preview.Value.RemovedPaths)
        );
    }

    [Fact]
    internal async Task PreviewRestoreAsync_WrongPassword_ReportsInvalidPassword()
    {
        await using var provider = TestHost.CreateProvider();
        var service = provider.GetRequiredService<IChunkedBackupService>();
        using var source = new TempDir();
        using var archive = new TempDir();
        _ = source.WriteText("a.txt", "alpha");
        await CreateArchiveAsync(service, source.Path, archive.Path);

        var preview = await service.PreviewRestoreAsync(
            archive.Path,
            "Not-The-Password-1",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(MessageCode.InvalidPassword, Assert.Single(preview.Errors).Code);
    }

    [Fact]
    internal async Task PreviewUpdateAsync_SourceThatDroppedAFile_ReportsItAsRemoved()
    {
        await using var provider = TestHost.CreateProvider();
        var service = provider.GetRequiredService<IChunkedBackupService>();
        using var source = new TempDir();
        using var archive = new TempDir();
        _ = source.WriteText("kept.txt", "kept");
        var dropped = source.WriteText("dropped.txt", "dropped");
        await CreateArchiveAsync(service, source.Path, archive.Path);
        File.Delete(dropped);

        var preview = await service.PreviewUpdateAsync(
            source.Path,
            archive.Path,
            Password,
            TestContext.Current.CancellationToken
        );

        Assert.Multiple(
            () => Assert.True(preview.IsSuccess),
            () => Assert.Equal(2, preview.Value.FileCount),
            () => Assert.Equal(11, preview.Value.TotalBytes),
            () => Assert.Equal(1, preview.Value.MatchedFiles),
            () => Assert.Equal(["dropped.txt"], preview.Value.RemovedPaths),
            () => Assert.Equal(0, preview.Value.BytesToProcess)
        );
    }

    [Fact]
    internal async Task PreviewUpdateAsync_FileWhoseSizeChanged_CountsItsNewSizeAsBytesToProcess()
    {
        await using var provider = TestHost.CreateProvider();
        var service = provider.GetRequiredService<IChunkedBackupService>();
        using var source = new TempDir();
        using var archive = new TempDir();
        _ = source.WriteText("same.txt", "same");
        _ = source.WriteText("grown.txt", "short");
        await CreateArchiveAsync(service, source.Path, archive.Path);
        _ = source.WriteText("grown.txt", "noticeably longer content");

        var preview = await service.PreviewUpdateAsync(
            source.Path,
            archive.Path,
            Password,
            TestContext.Current.CancellationToken
        );

        Assert.Multiple(
            () => Assert.True(preview.IsSuccess),
            () => Assert.Equal(2, preview.Value.MatchedFiles),
            () => Assert.Empty(preview.Value.RemovedPaths),
            () => Assert.Equal("noticeably longer content".Length, preview.Value.BytesToProcess)
        );
    }

    [Fact]
    internal async Task PreviewUpdateAsync_FolderWithoutAnArchive_ReportsTheManifestAnUpdateNeeds()
    {
        await using var provider = TestHost.CreateProvider();
        var service = provider.GetRequiredService<IChunkedBackupService>();
        using var source = new TempDir();
        using var empty = new TempDir();
        _ = source.WriteText("a.txt", "alpha");

        var preview = await service.PreviewUpdateAsync(
            source.Path,
            empty.Path,
            Password,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(MessageCode.ManifestRequiredForUpdate, Assert.Single(preview.Errors).Code);
    }

    [Fact]
    internal async Task PreviewRestoreAsync_FolderWithoutAnArchive_ReportsTheManifestARestoreNeeds()
    {
        await using var provider = TestHost.CreateProvider();
        var service = provider.GetRequiredService<IChunkedBackupService>();
        using var empty = new TempDir();

        var preview = await service.PreviewRestoreAsync(
            empty.Path,
            Password,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(MessageCode.ManifestRequiredForDecryption, Assert.Single(preview.Errors).Code);
    }

    [Fact]
    internal async Task PreviewUpdateAsync_SourceThatNoLongerExists_ReportsTheMissingSource()
    {
        await using var provider = TestHost.CreateProvider();
        var service = provider.GetRequiredService<IChunkedBackupService>();
        using var source = new TempDir();
        using var archive = new TempDir();
        _ = source.WriteText("a.txt", "alpha");
        await CreateArchiveAsync(service, source.Path, archive.Path);

        var preview = await service.PreviewUpdateAsync(
            Path.Join(source.Path, "gone"),
            archive.Path,
            Password,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(MessageCode.SourcePathNotExist, Assert.Single(preview.Errors).Code);
    }

    /// <summary>
    /// Creates an AES plus PBKDF2 archive of a folder and fails the test if the run did not succeed.
    /// </summary>
    /// <param name="service">The engine to create the archive with.</param>
    /// <param name="sourcePath">The folder to back up.</param>
    /// <param name="archivePath">The folder the archive is written into.</param>
    /// <returns>A task that completes once the archive exists.</returns>
    private static async Task CreateArchiveAsync(
        IChunkedBackupService service,
        string sourcePath,
        string archivePath
    )
    {
        var result = await service.CreateAsync(
            sourcePath,
            archivePath,
            Password,
            new ManifestHeader(
                EncryptionAlgorithm.Aes,
                KeyDerivationAlgorithm.PBKDF2,
                CompressionMode.None
            ),
            new RecordingProgress<BackupStatus>(),
            TestContext.Current.CancellationToken
        );

        Assert.True(
            result.IsSuccess && result.Value.IsSuccess,
            "The fixture could not create the backup under test."
        );
    }
}
