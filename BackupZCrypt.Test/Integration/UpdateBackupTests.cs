using BackupZCrypt.Application.Backup;
using BackupZCrypt.Domain.Algorithms;
using BackupZCrypt.Domain.Archive;
using BackupZCrypt.Test.Common;

using Microsoft.Extensions.DependencyInjection;

namespace BackupZCrypt.Test.Integration;

/// <summary>
/// Integration tests for the incremental update operation and restoring the updated backup.
/// </summary>
public sealed class UpdateBackupTests
{
    /// <summary>
    /// The password every backup in this fixture is created, updated, and restored with; long and
    /// varied enough to clear the validator's strength warnings.
    /// </summary>
    private const string Password = "Correct-Horse-Battery-Staple-42";

    /// <summary>
    /// The content shared by two files in different folders, so both entries reference one chunk and
    /// deleting only one of them must not reclaim it.
    /// </summary>
    private const string SharedContent = "shared twin content";

    [Fact]
    internal async Task Update_ThenRestore_ReflectsModifiedAndAddedFiles()
    {
        await using var provider = TestHost.CreateProvider();
        var operations = provider.GetRequiredService<IBackupOperations>();

        using var source = new TempDir();
        using var destination = new TempDir();
        using var restored = new TempDir();

        _ = source.WriteText("unchanged.txt", "stays the same");
        _ = source.WriteText("changing.txt", "original content");
        _ = source.WriteText(Path.Join("dir", "keep.txt"), "nested keep");

        var createResult = await operations.CreateAsync(
            NewCreateRequest(source.Path, destination.Path),
            cancellationToken: TestContext.Current.CancellationToken
        );
        Assert.Multiple(
            () => Assert.True(createResult.IsSuccess && createResult.Value.Completion!.IsSuccess),
            () => Assert.Equal(3, ChunkFiles(destination.Path).Length)
        );

        const string ModifiedContent = "MODIFIED content that is clearly different from the original";
        _ = source.WriteText("changing.txt", ModifiedContent);
        const string AddedContent = "freshly added file";
        _ = source.WriteText(Path.Join("dir", "added.txt"), AddedContent);

        var updateResult = await operations.UpdateAsync(
            NewUpdateRequest(source.Path, destination.Path),
            cancellationToken: TestContext.Current.CancellationToken
        );
        Assert.Multiple(
            () =>
                Assert.True(
                    updateResult.IsSuccess && updateResult.Value.Completion!.IsSuccess,
                    "Update did not succeed."
                ),
            () => Assert.Equal(2, updateResult.Value.Completion!.TotalFiles),
            () =>
                Assert.Equal(
                    updateResult.Value.Completion!.TotalFiles,
                    updateResult.Value.Completion!.ProcessedFiles
                ),
            () => Assert.Equal(4, ChunkFiles(destination.Path).Length)
        );

        var restoreResult = await operations.RestoreAsync(
            NewRestoreRequest(destination.Path, restored.Path),
            cancellationToken: TestContext.Current.CancellationToken
        );
        Assert.True(
            restoreResult.IsSuccess && restoreResult.Value.Completion!.IsSuccess,
            "Restore of the updated backup did not succeed."
        );

        // The four reads are hoisted out of the grouped assertion because Assert.Multiple takes
        // synchronous lambdas; they run only after the restore itself is known to have succeeded,
        // so a failed restore still reports the message above rather than a file-not-found error.
        var unchangedText = await File.ReadAllTextAsync(
            Path.Join(restored.Path, "unchanged.txt"),
            TestContext.Current.CancellationToken
        );
        var changingText = await File.ReadAllTextAsync(
            Path.Join(restored.Path, "changing.txt"),
            TestContext.Current.CancellationToken
        );
        var keepText = await File.ReadAllTextAsync(
            Path.Join(restored.Path, "dir", "keep.txt"),
            TestContext.Current.CancellationToken
        );
        var addedText = await File.ReadAllTextAsync(
            Path.Join(restored.Path, "dir", "added.txt"),
            TestContext.Current.CancellationToken
        );

        Assert.Multiple(
            () => Assert.Equal("stays the same", unchangedText),
            () => Assert.Equal(ModifiedContent, changingText),
            () => Assert.Equal("nested keep", keepText),
            () => Assert.Equal(AddedContent, addedText)
        );
    }

    [Fact]
    internal async Task Update_StoredChunkMissing_RegeneratesItFromUnchangedSource()
    {
        const string Content = "unchanged source content used to repair a missing chunk";

        await using var provider = TestHost.CreateProvider();
        var operations = provider.GetRequiredService<IBackupOperations>();

        using var source = new TempDir();
        using var destination = new TempDir();
        using var restored = new TempDir();

        _ = source.WriteText("only.txt", Content);
        var createResult = await operations.CreateAsync(
            NewCreateRequest(source.Path, destination.Path),
            cancellationToken: TestContext.Current.CancellationToken
        );
        Assert.True(createResult.IsSuccess && createResult.Value.Completion!.IsSuccess);

        var missingChunk = Assert.Single(ChunkFiles(destination.Path));
        File.Delete(missingChunk);

        var updateResult = await operations.UpdateAsync(
            NewUpdateRequest(source.Path, destination.Path),
            cancellationToken: TestContext.Current.CancellationToken
        );

        Assert.Multiple(
            () => Assert.True(
                updateResult.IsSuccess && updateResult.Value.Completion!.IsSuccess,
                "Update did not repair the missing chunk."
            ),
            () => Assert.Equal(1, updateResult.Value.Completion!.TotalFiles),
            () => Assert.Equal(1, updateResult.Value.Completion!.ProcessedFiles),
            () => _ = Assert.Single(ChunkFiles(destination.Path))
        );

        var restoreResult = await operations.RestoreAsync(
            NewRestoreRequest(destination.Path, restored.Path),
            cancellationToken: TestContext.Current.CancellationToken
        );
        Assert.True(
            restoreResult.IsSuccess && restoreResult.Value.Completion!.IsSuccess,
            "The repaired archive could not be restored."
        );

        var restoredContent = await File.ReadAllTextAsync(
            Path.Join(restored.Path, "only.txt"),
            TestContext.Current.CancellationToken
        );
        Assert.Equal(Content, restoredContent);
    }

    [Fact]
    internal async Task Update_SourceFileDeleted_DropsItsEntryAndPrunesOnlyTheChunksNothingElseUses()
    {
        await using var provider = TestHost.CreateProvider();
        var operations = provider.GetRequiredService<IBackupOperations>();

        using var source = new TempDir();
        using var destination = new TempDir();
        using var restored = new TempDir();

        var uniquePath = source.WriteText("unique.txt", "unique content");
        var twinPath = source.WriteText("twin-a.txt", SharedContent);
        var survivorRelativePath = Path.Join("dir", "twin-b.txt");
        _ = source.WriteText(survivorRelativePath, SharedContent);

        var createResult = await operations.CreateAsync(
            NewCreateRequest(source.Path, destination.Path),
            cancellationToken: TestContext.Current.CancellationToken
        );
        Assert.Multiple(
            () => Assert.True(createResult.IsSuccess && createResult.Value.Completion!.IsSuccess),
            () => Assert.Equal(3, createResult.Value.Completion!.TotalFiles),
            () => Assert.Equal(2, ChunkFiles(destination.Path).Length)
        );

        File.Delete(uniquePath);
        File.Delete(twinPath);

        var updateResult = await operations.UpdateAsync(
            NewUpdateRequest(source.Path, destination.Path),
            cancellationToken: TestContext.Current.CancellationToken
        );
        Assert.Multiple(
            () => Assert.True(updateResult.IsSuccess),
            () => Assert.True(updateResult.Value.Completion!.IsSuccess, "Update did not succeed."),
            () => Assert.Equal(0, updateResult.Value.Completion!.TotalFiles),
            () => _ = Assert.Single(ChunkFiles(destination.Path))
        );

        var restoreResult = await operations.RestoreAsync(
            NewRestoreRequest(destination.Path, restored.Path),
            cancellationToken: TestContext.Current.CancellationToken
        );
        Assert.True(
            restoreResult.IsSuccess && restoreResult.Value.Completion!.IsSuccess,
            "Restore after the pruning update did not succeed."
        );

        // Hoisted out of the grouped assertion for the same reason as in the test above: the read
        // only makes sense once the restore is known to have succeeded.
        var survivorText = await File.ReadAllTextAsync(
            Path.Join(restored.Path, survivorRelativePath),
            TestContext.Current.CancellationToken
        );

        Assert.Multiple(
            () => Assert.Equal(1, restoreResult.Value.Completion!.TotalFiles),
            () => _ = Assert.Single(Directory.GetFiles(restored.Path, "*", SearchOption.AllDirectories)),
            () => Assert.Equal(SharedContent, survivorText)
        );
    }

    /// <summary>
    /// Lists the encrypted chunk files stored under a backup root.
    /// </summary>
    /// <param name="backupRoot">The directory a backup was written to.</param>
    /// <returns>The absolute paths of the stored chunk files.</returns>
    private static string[] ChunkFiles(string backupRoot)
    {
        return Directory.GetFiles(
            Path.Join(backupRoot, BackupConstants.ChunksDirectoryName),
            "*" + BackupConstants.AppFileExtension
        );
    }

    /// <summary>
    /// Builds an AES, PBKDF2, and Zstd create request that proceeds past advisory warnings.
    /// </summary>
    /// <param name="sourcePath">The tree to back up.</param>
    /// <param name="destinationPath">The directory the backup is written to.</param>
    /// <returns>The assembled request.</returns>
    private static CreateBackupRequest NewCreateRequest(string sourcePath, string destinationPath)
    {
        return new CreateBackupRequest(
            sourcePath,
            destinationPath,
            Password,
            Password,
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.PBKDF2,
            CompressionMode.Zstd,
            ProceedOnWarnings: true
        );
    }

    /// <summary>
    /// Builds an update request that proceeds past advisory warnings; the archive's own
    /// algorithms govern the run.
    /// </summary>
    /// <param name="sourcePath">The tree whose current contents feed the update.</param>
    /// <param name="backupPath">The existing backup directory to update.</param>
    /// <returns>The assembled request.</returns>
    private static UpdateBackupRequest NewUpdateRequest(string sourcePath, string backupPath)
    {
        return new UpdateBackupRequest(sourcePath, backupPath, Password, ProceedOnWarnings: true);
    }

    /// <summary>
    /// Builds a restore request that proceeds past advisory warnings; the archive's own
    /// algorithms govern the run.
    /// </summary>
    /// <param name="backupPath">The backup directory to restore from.</param>
    /// <param name="destinationPath">The directory the restored files are written to.</param>
    /// <returns>The assembled request.</returns>
    private static RestoreBackupRequest NewRestoreRequest(string backupPath, string destinationPath)
    {
        return new RestoreBackupRequest(backupPath, destinationPath, Password, ProceedOnWarnings: true);
    }
}
