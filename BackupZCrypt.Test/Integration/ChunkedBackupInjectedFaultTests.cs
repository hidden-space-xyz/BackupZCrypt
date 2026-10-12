using System.Security.Cryptography;
using System.Text;

using BackupZCrypt.Application.Algorithms;
using BackupZCrypt.Application.Common;
using BackupZCrypt.Application.Engine;
using BackupZCrypt.Application.FileSystem;
using BackupZCrypt.Application.Manifest;
using BackupZCrypt.Domain.Algorithms;
using BackupZCrypt.Domain.Archive;
using BackupZCrypt.Infrastructure.FileSystem;
using BackupZCrypt.Test.Common;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

namespace BackupZCrypt.Test.Integration;

/// <summary>
/// Integration tests for the chunked backup engine when one collaborator misbehaves: a manifest that
/// cannot be read or saved, file metadata that cannot be read or applied, a source walk that meets
/// an unreadable folder or a link, and a manifest whose entries no longer match the stored data.
/// </summary>
/// <remarks>
/// <para>
/// Each fault is injected by registering, after the production services, a thin wrapper around the
/// real implementation that fails only where the case says so. Everything else, from key derivation to
/// the bytes on disk, runs through the real stack, and every case asserts what the user is left with,
/// not only the reported counts.
/// </para>
/// <para>
/// A manifest is edited through <see cref="IManifestService.SaveChunkManifestAsync"/> with the keys
/// derived from the archive's own password, so the edited archive still authenticates and is refused
/// or accepted for the reason under test rather than for a broken tag.
/// </para>
/// </remarks>
public sealed class ChunkedBackupInjectedFaultTests
{
    /// <summary>
    /// The password every backup in this fixture is created and opened with.
    /// </summary>
    private const string Password = "Correct-Horse-Battery-Staple-42";

    /// <summary>
    /// The algorithms every backup in this fixture is created with: AES with PBKDF2, the cheapest key
    /// derivation the app offers, and no compression.
    /// </summary>
    private static readonly ManifestHeader Algorithms = new(
        EncryptionAlgorithm.Aes,
        KeyDerivationAlgorithm.PBKDF2,
        CompressionMode.None
    );

    /// <summary>
    /// Supplies every way a manifest can be unreadable together with the message the user must see.
    /// </summary>
    /// <returns>The inspected manifest kind and the expected error code.</returns>
    public static TheoryData<ManifestKind, MessageCode> UnreadableManifests()
    {
        return new()
        {
            { ManifestKind.Missing, MessageCode.ManifestRequiredForVerify },
            { ManifestKind.PathNotFound, MessageCode.ManifestRequiredForVerify },
            { ManifestKind.NotADirectory, MessageCode.ManifestRequiredForVerify },
            { ManifestKind.Unsupported, MessageCode.ManifestUnsupported },
            { ManifestKind.Encrypted, MessageCode.ManifestDamaged },
            { ManifestKind.Damaged, MessageCode.ManifestDamaged },
            { (ManifestKind)99, MessageCode.ManifestDamaged },
        };
    }

    [Theory]
    [MemberData(nameof(UnreadableManifests))]
    internal async Task Verify_ManifestCannotBeRead_ExplainsWhyFromWhatTheFolderHolds(
        ManifestKind kind,
        MessageCode expected
    )
    {
        var manifestService = Substitute.For<IManifestService>();
        _ = manifestService
            .ReadChunkManifestPreambleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ManifestPreamble?>(null));
        _ = manifestService.InspectManifestAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(kind);

        await using var provider = TestHost.CreateProvider(services => services.AddSingleton(manifestService));
        var service = provider.GetRequiredService<IChunkedBackupService>();
        using var archive = new TempDir();

        var result = await service.VerifyAsync(
            archive.Path,
            Password,
            new RecordingProgress<BackupStatus>(),
            TestContext.Current.CancellationToken
        );

        Assert.Multiple(
            () => Assert.False(result.IsSuccess),
            () => Assert.Equal([expected], result.Errors.Select(static e => e.Code))
        );
    }

    [Fact]
    internal async Task Create_SourceFolderMissing_FailsWithSourcePathNotExist()
    {
        await using var provider = TestHost.CreateProvider();
        var service = provider.GetRequiredService<IChunkedBackupService>();
        using var archive = new TempDir();

        var result = await service.CreateAsync(
            archive.Combine("no-such-source"),
            archive.Path,
            Password,
            Algorithms,
            new RecordingProgress<BackupStatus>(),
            TestContext.Current.CancellationToken
        );

        Assert.Multiple(
            () => Assert.False(result.IsSuccess),
            () => Assert.Equal([MessageCode.SourcePathNotExist], result.Errors.Select(static e => e.Code)),
            () => Assert.Empty(Directory.GetFileSystemEntries(archive.Path))
        );
    }

    [Fact]
    internal async Task Update_SourceFolderRemovedSinceTheBackup_FailsAndLeavesTheArchiveUnchanged()
    {
        await using var provider = TestHost.CreateProvider();
        var service = provider.GetRequiredService<IChunkedBackupService>();
        using var source = new TempDir();
        using var archive = new TempDir();

        _ = source.WriteText("a.txt", "content backed up before its folder disappears");
        await CreateBackupAsync(service, source.Path, archive.Path);
        var archiveBefore = Snapshot(archive.Path);

        var result = await service.UpdateAsync(
            source.Combine("gone"),
            archive.Path,
            Password,
            new RecordingProgress<BackupStatus>(),
            TestContext.Current.CancellationToken
        );

        Assert.Multiple(
            () => Assert.False(result.IsSuccess),
            () => Assert.Equal([MessageCode.SourcePathNotExist], result.Errors.Select(static e => e.Code)),
            () => Assert.Equal(archiveBefore, Snapshot(archive.Path))
        );
    }

    [Fact]
    internal async Task Create_UnexpectedFailureWhileReadingAFile_FailsTheBackupAndWritesNoManifest()
    {
        InterceptedMetadataService metadata = new(
            onRead: static (_, _) => throw new InvalidOperationException("injected engine fault")
        );
        await using var provider = TestHost.CreateProvider(services =>
            services.AddSingleton<IFileMetadataService>(metadata)
        );
        var service = provider.GetRequiredService<IChunkedBackupService>();
        using var source = new TempDir();
        using var archive = new TempDir();

        _ = source.WriteText("a.txt", "content the engine never gets to read");
        _ = source.WriteText(Path.Join("dir", "b.txt"), "nested content the engine never gets to read");

        var result = await service.CreateAsync(
            source.Path,
            archive.Path,
            Password,
            Algorithms,
            new RecordingProgress<BackupStatus>(),
            TestContext.Current.CancellationToken
        );

        Assert.Multiple(
            () => Assert.False(result.IsSuccess, "An engine fault was reported as a per-file failure."),
            () => Assert.Equal(MessageCode.UnexpectedErrorFormat, Assert.Single(result.Errors).Code),
            () => Assert.Equal<object>(["injected engine fault"], result.Errors[0].Args),
            () => Assert.False(File.Exists(Path.Join(archive.Path, BackupConstants.ManifestFileName))),
            () => Assert.Empty(ChunkFiles(archive.Path))
        );
    }

    [Fact]
    internal async Task CreateThenUpdate_SourceHoldsALink_WarnsThatItWasLeftOut()
    {
        using var source = new TempDir();
        using var archive = new TempDir();
        var link = source.Combine("linked-folder");
        InterceptedScanService scan = new(found => found with { SkippedLinks = [.. found.SkippedLinks, link] });

        await using var provider = TestHost.CreateProvider(services =>
            services.AddSingleton<IDirectoryScanService>(scan)
        );
        var service = provider.GetRequiredService<IChunkedBackupService>();

        _ = source.WriteText("a.txt", "a file next to a link");

        var created = await service.CreateAsync(
            source.Path,
            archive.Path,
            Password,
            Algorithms,
            new RecordingProgress<BackupStatus>(),
            TestContext.Current.CancellationToken
        );

        _ = source.WriteText("a.txt", "a file next to a link, revised");

        var updated = await service.UpdateAsync(
            source.Path,
            archive.Path,
            Password,
            new RecordingProgress<BackupStatus>(),
            TestContext.Current.CancellationToken
        );

        Assert.Multiple(
            () => Assert.True(created.IsSuccess && created.Value.IsSuccess),
            () => Assert.True(updated.IsSuccess && updated.Value.IsSuccess),
            () => Assert.Equal([MessageCode.SourceLinksSkippedFormat], created.Value.Warnings.Select(static w => w.Code)),
            () => Assert.Equal([MessageCode.SourceLinksSkippedFormat], updated.Value.Warnings.Select(static w => w.Code)),
            () => Assert.Equal<object>([1, PathSample.Of(["linked-folder"])], updated.Value.Warnings[0].Args)
        );
    }

    [Fact]
    internal async Task Update_FolderUnreadableThisTime_KeepsItsRecordedFilesAndEmptyFolders()
    {
        const string LockedContent = "content inside a folder that cannot be read during the update";
        const string RevisedContent = "revised content outside the unreadable folder";

        using var source = new TempDir();
        using var archive = new TempDir();
        using var restored = new TempDir();

        _ = source.WriteText("keep.txt", "the original content outside the unreadable folder");
        _ = source.WriteText(Path.Join("locked", "inner.txt"), LockedContent);
        _ = Directory.CreateDirectory(source.Combine("locked", "empty"));

        await using (var plain = TestHost.CreateProvider())
        {
            await CreateBackupAsync(plain.GetRequiredService<IChunkedBackupService>(), source.Path, archive.Path);
        }

        _ = source.WriteText("keep.txt", RevisedContent);
        var locked = source.Combine("locked");
        InterceptedScanService scan = new(found => found with
        {
            Files = [.. found.Files.Where(f => !IsBelow(locked, f))],
            EmptyDirectories = [.. found.EmptyDirectories.Where(d => !IsBelow(locked, d))],
            InaccessibleDirectories = [new InaccessibleDirectory(locked, new UnauthorizedAccessException("denied"))],
        });

        await using var provider = TestHost.CreateProvider(services =>
            services.AddSingleton<IDirectoryScanService>(scan)
        );
        var service = provider.GetRequiredService<IChunkedBackupService>();

        var result = await service.UpdateAsync(
            source.Path,
            archive.Path,
            Password,
            new RecordingProgress<BackupStatus>(),
            TestContext.Current.CancellationToken
        );

        var restoreResult = await service.RestoreAsync(
            archive.Path,
            restored.Path,
            Password,
            new RecordingProgress<BackupStatus>(),
            TestContext.Current.CancellationToken
        );

        Assert.Multiple(
            () => Assert.True(result.IsSuccess, "An unreadable folder failed the whole update."),
            () => Assert.Equal([MessageCode.InaccessibleFolderFormat], result.Value.Errors.Select(static e => e.Code)),
            () => Assert.True(restoreResult.IsSuccess && restoreResult.Value.IsSuccess),
            () => Assert.Equal(RevisedContent, File.ReadAllText(restored.Combine("keep.txt"))),
            () => Assert.Equal(LockedContent, File.ReadAllText(restored.Combine("locked", "inner.txt"))),
            () =>
                Assert.True(
                    Directory.Exists(restored.Combine("locked", "empty")),
                    "The update dropped an empty folder it could not see this time."
                )
        );
    }

    [Fact]
    internal async Task Update_MetadataOfChangedFilesCannotBeRead_KeepsTheirPreviousVersionAndCommitsTheRest()
    {
        const string OriginalContent = "the original content whose revision cannot be inspected";
        const string RevisedOther = "a revision that is inspected and committed";

        using var source = new TempDir();
        using var archive = new TempDir();
        using var restored = new TempDir();

        _ = source.WriteText("stuck.txt", OriginalContent);
        _ = source.WriteText("other.txt", "the original content of a file that is revised normally");

        await using (var plain = TestHost.CreateProvider())
        {
            await CreateBackupAsync(plain.GetRequiredService<IChunkedBackupService>(), source.Path, archive.Path);
        }

        _ = source.WriteText("stuck.txt", "a revision that is never captured");
        _ = source.WriteText("other.txt", RevisedOther);
        _ = source.WriteText("new.txt", "a new file that is never captured");

        InterceptedMetadataService metadata = new(
            onRead: static (path, found) =>
                Path.GetFileName(path) is "stuck.txt" or "new.txt" ? throw new IOException("injected read fault") : found
        );
        await using var provider = TestHost.CreateProvider(services =>
            services.AddSingleton<IFileMetadataService>(metadata)
        );
        var service = provider.GetRequiredService<IChunkedBackupService>();

        var result = await service.UpdateAsync(
            source.Path,
            archive.Path,
            Password,
            new RecordingProgress<BackupStatus>(),
            TestContext.Current.CancellationToken
        );

        var restoreResult = await service.RestoreAsync(
            archive.Path,
            restored.Path,
            Password,
            new RecordingProgress<BackupStatus>(),
            TestContext.Current.CancellationToken
        );

        Assert.Multiple(
            () => Assert.True(result.IsSuccess, "Two unreadable files failed the whole update."),
            () => Assert.Equal(3, result.Value.TotalFiles),
            () => Assert.Equal(1, result.Value.ProcessedFiles),
            () => Assert.Equal<object>(["new.txt", "stuck.txt"], result.Value.Errors.Select(static e => e.Args[0])),
            () => Assert.All(result.Value.Errors, static e => Assert.Equal(MessageCode.FileBackupErrorFormat, e.Code)),
            () => Assert.True(restoreResult.IsSuccess && restoreResult.Value.IsSuccess),
            () => Assert.Equal(OriginalContent, File.ReadAllText(restored.Combine("stuck.txt"))),
            () => Assert.Equal(RevisedOther, File.ReadAllText(restored.Combine("other.txt"))),
            () => Assert.False(File.Exists(restored.Combine("new.txt")))
        );
    }

    [Fact]
    internal async Task Update_ManifestCannotBeSaved_FailsAndLeavesTheArchiveExactlyAsItWas()
    {
        using var source = new TempDir();
        using var archive = new TempDir();

        _ = source.WriteText("a.txt", "the archived content");

        await using (var plain = TestHost.CreateProvider())
        {
            await CreateBackupAsync(plain.GetRequiredService<IChunkedBackupService>(), source.Path, archive.Path);
        }

        _ = source.WriteText("a.txt", "a revision whose chunks are written and then discarded");
        _ = source.WriteText("b.txt", "a new file whose chunks are written and then discarded");
        var archiveBefore = Snapshot(archive.Path);

        await using var provider = TestHost.CreateProvider(services =>
            services.AddSingleton<IManifestService>(sp =>
                new UnsavableManifestService(ActivatorUtilities.CreateInstance<ManifestService>(sp))
            )
        );
        var service = provider.GetRequiredService<IChunkedBackupService>();

        var result = await service.UpdateAsync(
            source.Path,
            archive.Path,
            Password,
            new RecordingProgress<BackupStatus>(),
            TestContext.Current.CancellationToken
        );

        Assert.Multiple(
            () => Assert.False(result.IsSuccess, "An update whose manifest was never saved reported a result."),
            () => Assert.Equal([MessageCode.ManifestWriteFailedFormat], result.Errors.Select(static e => e.Code)),
            () => Assert.Equal(archiveBefore, Snapshot(archive.Path))
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    internal async Task Restore_MetadataCannotBeApplied_KeepsTheContentOrFailsOnAnEngineFault(bool engineFault)
    {
        const string Content = "content that is restored even if its timestamps cannot be";

        using var source = new TempDir();
        using var archive = new TempDir();
        using var restored = new TempDir();

        _ = source.WriteText("a.txt", Content);

        await using (var plain = TestHost.CreateProvider())
        {
            await CreateBackupAsync(plain.GetRequiredService<IChunkedBackupService>(), source.Path, archive.Path);
        }

        InterceptedMetadataService metadata = new(
            onApply: _ =>
                throw (engineFault
                    ? new InvalidOperationException("injected engine fault")
                    : new IOException("this file system keeps no timestamps"))
        );
        await using var provider = TestHost.CreateProvider(services =>
            services.AddSingleton<IFileMetadataService>(metadata)
        );
        var service = provider.GetRequiredService<IChunkedBackupService>();

        var result = await service.RestoreAsync(
            archive.Path,
            restored.Path,
            Password,
            new RecordingProgress<BackupStatus>(),
            TestContext.Current.CancellationToken
        );

        if (engineFault)
        {
            Assert.Multiple(
                () => Assert.False(result.IsSuccess, "An engine fault was reported as a restore result."),
                () => Assert.Equal(MessageCode.UnexpectedErrorFormat, Assert.Single(result.Errors).Code),
                () => Assert.Equal<object>(["injected engine fault"], result.Errors[0].Args)
            );
        }
        else
        {
            Assert.Multiple(
                () => Assert.True(result.IsSuccess && result.Value.IsSuccess),
                () => Assert.Equal(Content, File.ReadAllText(restored.Combine("a.txt")))
            );
        }
    }

    [Fact]
    internal async Task Restore_IntoAFolderThatHoldsTheBackup_RefusesOnlyTheEntriesThatWouldLandInsideIt()
    {
        const string KeepContent = "a file that lands next to the backup folder";

        await using var provider = TestHost.CreateProvider();
        var service = provider.GetRequiredService<IChunkedBackupService>();

        using var source = new TempDir();
        using var restored = new TempDir();
        var archive = restored.Combine("vault");

        _ = source.WriteText("keep.txt", KeepContent);
        _ = source.WriteText(Path.Join("vault", "inner.txt"), "a file that would land inside the backup folder");
        _ = Directory.CreateDirectory(source.Combine("vault", "empty"));
        await CreateBackupAsync(service, source.Path, archive);
        var archiveBefore = Snapshot(archive);

        var result = await service.RestoreAsync(
            archive,
            restored.Path,
            Password,
            new RecordingProgress<BackupStatus>(),
            TestContext.Current.CancellationToken
        );

        Assert.Multiple(
            () => Assert.True(result.IsSuccess, "Two refused entries failed the whole restore."),
            () => Assert.Equal(1, result.Value.ProcessedFiles),
            () =>
                Assert.Equal(
                    [MessageCode.RestoreTargetInsideBackupFormat, MessageCode.RestoreTargetInsideBackupFormat],
                    result.Value.Errors.Select(static e => e.Code)
                ),
            () =>
                Assert.Equal<object>(["vault/empty", "vault/inner.txt"], result.Value.Errors.Select(static e => e.Args[0])),
            () => Assert.Equal(KeepContent, File.ReadAllText(restored.Combine("keep.txt"))),
            () => Assert.Equal(archiveBefore, Snapshot(archive))
        );
    }

    [Fact]
    internal async Task Restore_FolderBlockedByAnExistingFile_ReportsThatFolderAndRestoresTheFiles()
    {
        const string ReadmeContent = "a file restored next to a folder that cannot be created";
        const string BlockingContent = "a file that already occupies the name of a recorded folder";

        await using var provider = TestHost.CreateProvider();
        var service = provider.GetRequiredService<IChunkedBackupService>();

        using var source = new TempDir();
        using var archive = new TempDir();
        using var restored = new TempDir();

        _ = source.WriteText(Path.Join("docs", "readme.txt"), ReadmeContent);
        _ = Directory.CreateDirectory(source.Combine("empty"));
        await CreateBackupAsync(service, source.Path, archive.Path);
        _ = restored.WriteText("empty", BlockingContent);

        var result = await service.RestoreAsync(
            archive.Path,
            restored.Path,
            Password,
            new RecordingProgress<BackupStatus>(),
            TestContext.Current.CancellationToken
        );

        Assert.Multiple(
            () => Assert.True(result.IsSuccess, "One folder that cannot be created failed the whole restore."),
            () => Assert.Equal(MessageCode.DecryptionErrorFormat, Assert.Single(result.Value.Errors).Code),
            () => Assert.Equal("empty", result.Value.Errors[0].Args[0]),
            () => Assert.Equal(ReadmeContent, File.ReadAllText(restored.Combine("docs", "readme.txt"))),
            () => Assert.Equal(BlockingContent, File.ReadAllText(restored.Combine("empty")))
        );
    }

    [Fact]
    internal async Task Restore_TwoEntriesOnlyDifferingInLetterCase_FollowsThePlatformRule()
    {
        await using var provider = TestHost.CreateProvider();
        var service = provider.GetRequiredService<IChunkedBackupService>();

        using var source = new TempDir();
        using var archive = new TempDir();
        using var restored = new TempDir();

        _ = source.WriteText("a.txt", "lower");
        _ = source.WriteText("b.txt", "upper");
        await CreateBackupAsync(service, source.Path, archive.Path);
        await RewriteManifestAsync(
            provider,
            archive.Path,
            static m => m with
            {
                Files = [.. m.Files.Select(static f => f.OriginalPath is "b.txt" ? f with { OriginalPath = "A.txt" } : f)],
            }
        );

        var result = await service.RestoreAsync(
            archive.Path,
            restored.Path,
            Password,
            new RecordingProgress<BackupStatus>(),
            TestContext.Current.CancellationToken
        );

        Assert.True(result.IsSuccess);

        if (PathNormalizationHelper.PathComparer is StringComparison.OrdinalIgnoreCase)
        {
            var conflict = Assert.Single(result.Value.Errors);
            Assert.Multiple(
                () => Assert.Equal(MessageCode.RestoreNameConflictFormat, conflict.Code),
                () => Assert.Equal<object>(["a.txt", "A.txt"], conflict.Args),
                () => Assert.Equal(1, result.Value.ProcessedFiles),
                () => Assert.Equal("upper", File.ReadAllText(restored.Combine("A.txt")))
            );
        }
        else
        {
            Assert.Multiple(
                () => Assert.Empty(result.Value.Errors),
                () => Assert.Equal("lower", File.ReadAllText(restored.Combine("a.txt"))),
                () => Assert.Equal("upper", File.ReadAllText(restored.Combine("A.txt")))
            );
        }
    }

    [Fact]
    internal async Task Verify_FileWhoseRecordedHashNoLongerMatches_IsReportedWithoutSettingItsChunksAside()
    {
        await using var provider = TestHost.CreateProvider();
        var service = provider.GetRequiredService<IChunkedBackupService>();

        using var source = new TempDir();
        using var archive = new TempDir();

        _ = source.WriteText("a.txt", "content whose recorded hash stays correct");
        _ = source.WriteText("b.txt", "content whose recorded hash is replaced");
        await CreateBackupAsync(service, source.Path, archive.Path);

        var foreignHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes("something else")));
        await RewriteManifestAsync(
            provider,
            archive.Path,
            m => m with
            {
                Files = [.. m.Files.Select(f => f.OriginalPath is "b.txt" ? f with { FileHash = foreignHash } : f)],
            }
        );
        var archiveBefore = Snapshot(archive.Path);

        var result = await service.VerifyAsync(
            archive.Path,
            Password,
            new RecordingProgress<BackupStatus>(),
            TestContext.Current.CancellationToken
        );

        var error = Assert.Single(result.Value.Errors);
        Assert.Multiple(
            () => Assert.Equal(1, result.Value.ProcessedFiles),
            () => Assert.Equal(MessageCode.IntegrityErrorFormat, error.Code),
            () => Assert.Equal("b.txt", error.Args[0]),
            () => Assert.Equal(MessageCode.ReasonDataCorrupted, Assert.IsType<LocalizableMessage>(error.Args[1]).Code),
            () => Assert.Empty(result.Value.Warnings),
            () => Assert.Equal(archiveBefore, Snapshot(archive.Path))
        );
    }

    [Fact]
    internal async Task PreviewUpdate_FileWhoseMetadataCannotBeRead_IsLeftOutOfTheBytesToProcess()
    {
        using var source = new TempDir();
        using var archive = new TempDir();

        _ = source.WriteText("readable.txt", "original");
        _ = source.WriteText("unreadable.txt", "original");

        await using (var plain = TestHost.CreateProvider())
        {
            await CreateBackupAsync(plain.GetRequiredService<IChunkedBackupService>(), source.Path, archive.Path);
        }

        var readable = source.WriteText("readable.txt", "revised and measurable");
        _ = source.WriteText("unreadable.txt", "revised but never measured");

        InterceptedMetadataService metadata = new(
            onRead: static (path, found) =>
                Path.GetFileName(path) is "unreadable.txt" ? throw new UnauthorizedAccessException("denied") : found
        );
        await using var provider = TestHost.CreateProvider(services =>
            services.AddSingleton<IFileMetadataService>(metadata)
        );
        var service = provider.GetRequiredService<IChunkedBackupService>();

        var preview = await service.PreviewUpdateAsync(
            source.Path,
            archive.Path,
            Password,
            TestContext.Current.CancellationToken
        );

        Assert.Multiple(
            () => Assert.True(preview.IsSuccess),
            () => Assert.Equal(2, preview.Value.MatchedFiles),
            () => Assert.Equal(new FileInfo(readable).Length, preview.Value.BytesToProcess)
        );
    }

    [Fact]
    internal async Task Preview_SizesBeyondWhatALongHolds_SaturateInsteadOfWrapping()
    {
        using var source = new TempDir();
        using var archive = new TempDir();

        _ = source.WriteText("a.txt", "first");
        _ = source.WriteText("b.txt", "second");

        await using (var plain = TestHost.CreateProvider())
        {
            await CreateBackupAsync(plain.GetRequiredService<IChunkedBackupService>(), source.Path, archive.Path);
            await RewriteManifestAsync(
                plain,
                archive.Path,
                static m => m with { Files = [.. m.Files.Select(static f => f with { TotalSize = long.MaxValue - 1 })] }
            );
        }

        InterceptedMetadataService metadata = new(
            onRead: static (_, found) => found with { Size = long.MaxValue - 2 }
        );
        await using var provider = TestHost.CreateProvider(services =>
            services.AddSingleton<IFileMetadataService>(metadata)
        );
        var service = provider.GetRequiredService<IChunkedBackupService>();

        var update = await service.PreviewUpdateAsync(
            source.Path,
            archive.Path,
            Password,
            TestContext.Current.CancellationToken
        );
        var restore = await service.PreviewRestoreAsync(archive.Path, Password, TestContext.Current.CancellationToken);

        Assert.Multiple(
            () => Assert.Equal(long.MaxValue, update.Value.BytesToProcess),
            () => Assert.Equal(long.MaxValue, update.Value.TotalBytes),
            () => Assert.Equal(long.MaxValue, restore.Value.TotalBytes)
        );
    }

    /// <summary>
    /// Creates a backup through the real engine and fails the test if it did not fully succeed, so a
    /// broken fixture never masquerades as a fault-handling defect.
    /// </summary>
    /// <param name="service">The chunked backup service to create it with.</param>
    /// <param name="sourcePath">The directory to back up.</param>
    /// <param name="archivePath">The directory the backup is written to.</param>
    /// <returns>A task that completes once the backup exists.</returns>
    private static async Task CreateBackupAsync(IChunkedBackupService service, string sourcePath, string archivePath)
    {
        var result = await service.CreateAsync(
            sourcePath,
            archivePath,
            Password,
            Algorithms,
            new RecordingProgress<BackupStatus>(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess && result.Value.IsSuccess, "The fixture could not create the backup under test.");
    }

    /// <summary>
    /// Decrypts the manifest of a backup, passes it through <paramref name="rewrite"/>, and saves the
    /// result with the archive's own keys, so the edited manifest still authenticates.
    /// </summary>
    /// <param name="provider">The provider holding the real manifest service and key derivation strategies.</param>
    /// <param name="backupRoot">The backup whose manifest is edited.</param>
    /// <param name="rewrite">Maps the decrypted manifest to the one saved in its place.</param>
    /// <returns>A task that completes once the edited manifest has been saved.</returns>
    private static async Task RewriteManifestAsync(
        ServiceProvider provider,
        string backupRoot,
        Func<ChunkManifestData, ChunkManifestData> rewrite
    )
    {
        var manifestService = provider.GetRequiredService<IManifestService>();
        var preamble = await manifestService.ReadChunkManifestPreambleAsync(backupRoot, CancellationToken.None);
        Assert.NotNull(preamble);

        using var keys = ArchiveKeys.Derive(
            Password,
            preamble.MasterSalt,
            provider
                .GetRequiredService<IStrategyRegistry<KeyDerivationAlgorithm, IKeyDerivationAlgorithmStrategy>>()
                .Resolve(preamble.KeyDerivation)
        );

        var manifest = manifestService.DecryptChunkManifest(preamble, keys.ManifestEncryptionKey);
        Assert.NotNull(manifest);

        var errors = await manifestService.SaveChunkManifestAsync(
            rewrite(manifest),
            backupRoot,
            keys.ManifestEncryptionKey,
            preamble.Algorithm,
            CancellationToken.None
        );

        Assert.Empty(errors);
    }

    /// <summary>
    /// Determines whether a path lies strictly below a folder.
    /// </summary>
    /// <param name="folder">The folder.</param>
    /// <param name="path">The path to test.</param>
    /// <returns><see langword="true"/> when <paramref name="path"/> is inside <paramref name="folder"/>.</returns>
    private static bool IsBelow(string folder, string path)
    {
        return path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    /// <summary>
    /// Lists the encrypted chunk files stored under an archive root.
    /// </summary>
    /// <param name="archiveRoot">The directory a backup was written to.</param>
    /// <returns>The chunk files found, or none when the chunks directory does not exist.</returns>
    private static string[] ChunkFiles(string archiveRoot)
    {
        var chunksDirectory = Path.Join(archiveRoot, BackupConstants.ChunksDirectoryName);

        return Directory.Exists(chunksDirectory)
            ? Directory.GetFiles(chunksDirectory, "*", SearchOption.AllDirectories)
            : [];
    }

    /// <summary>
    /// Captures the exact content of every file under a directory, so a later comparison detects any
    /// write, deletion, rename, or addition.
    /// </summary>
    /// <param name="root">The directory to snapshot.</param>
    /// <returns>One ordered entry per file, pairing its relative path with the hash of its bytes.</returns>
    private static string[] Snapshot(string root)
    {
        return
        [
            .. Directory
                .GetFiles(root, "*", SearchOption.AllDirectories)
                .Select(file =>
                    Path.GetRelativePath(root, file).Replace('\\', '/')
                    + "|"
                    + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)))
                )
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// The real metadata service with optional hooks that can replace or refuse what it reads and
    /// refuse what it applies.
    /// </summary>
    /// <param name="onRead">Maps each path and its real metadata to what the engine sees; may throw.</param>
    /// <param name="onApply">Runs before metadata is applied to a restored file; may throw.</param>
    private sealed class InterceptedMetadataService(
        Func<string, FileMetadata, FileMetadata>? onRead = null,
        Action<string>? onApply = null
    ) : IFileMetadataService
    {
        /// <summary>
        /// The production service every call is forwarded to.
        /// </summary>
        private readonly FileMetadataService inner = new();

        /// <inheritdoc/>
        public FileMetadata GetFileMetadata(string filePath)
        {
            var metadata = this.inner.GetFileMetadata(filePath);
            return onRead is null ? metadata : onRead(filePath, metadata);
        }

        /// <inheritdoc/>
        public void ApplyFileMetadata(
            string filePath,
            DateTime? lastWriteTimeUtc,
            bool? isReadOnly,
            bool? isHidden,
            int? unixMode
        )
        {
            onApply?.Invoke(filePath);
            this.inner.ApplyFileMetadata(filePath, lastWriteTimeUtc, isReadOnly, isHidden, unixMode);
        }
    }

    /// <summary>
    /// The real directory walker whose findings are adjusted before the engine sees them.
    /// </summary>
    /// <param name="adjust">Maps the real walk to the one the engine receives.</param>
    private sealed class InterceptedScanService(Func<DirectoryScan, DirectoryScan> adjust) : IDirectoryScanService
    {
        /// <summary>
        /// The production walker every call is forwarded to.
        /// </summary>
        private readonly DirectoryScanService inner = new();

        /// <inheritdoc/>
        public async Task<DirectoryScan> ScanDirectoryAsync(
            string directoryPath,
            CancellationToken cancellationToken = default
        )
        {
            return adjust(await this.inner.ScanDirectoryAsync(directoryPath, cancellationToken));
        }
    }

    /// <summary>
    /// The real manifest service, except that saving a manifest always fails.
    /// </summary>
    /// <param name="inner">The production service every read is forwarded to.</param>
    private sealed class UnsavableManifestService(IManifestService inner) : IManifestService
    {
        /// <inheritdoc/>
        public Task<ManifestKind> DetectManifestKindAsync(string backupPath, CancellationToken cancellationToken = default)
        {
            return inner.DetectManifestKindAsync(backupPath, cancellationToken);
        }

        /// <inheritdoc/>
        public Task<ManifestKind> InspectManifestAsync(string backupRoot, CancellationToken cancellationToken = default)
        {
            return inner.InspectManifestAsync(backupRoot, cancellationToken);
        }

        /// <inheritdoc/>
        public Task<ManifestPreamble?> ReadChunkManifestPreambleAsync(
            string sourceRoot,
            CancellationToken cancellationToken
        )
        {
            return inner.ReadChunkManifestPreambleAsync(sourceRoot, cancellationToken);
        }

        /// <inheritdoc/>
        public ChunkManifestData? DecryptChunkManifest(ManifestPreamble preamble, byte[] encryptionKey)
        {
            return inner.DecryptChunkManifest(preamble, encryptionKey);
        }

        /// <inheritdoc/>
        public Task<IReadOnlyList<LocalizableMessage>> SaveChunkManifestAsync(
            ChunkManifestData manifestData,
            string destinationRoot,
            byte[] encryptionKey,
            EncryptionAlgorithm algorithm,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult<IReadOnlyList<LocalizableMessage>>(
                [new LocalizableMessage(MessageCode.ManifestWriteFailedFormat, "injected failure")]
            );
        }
    }
}
