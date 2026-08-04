using BackupZCrypt.Application.Orchestrators.Interfaces;
using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.ValueObjects.Backup;
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

    [Test]
    public async Task Update_ThenRestore_ReflectsModifiedAndAddedFiles()
    {
        await using var provider = TestHost.CreateProvider();
        var orchestrator = provider.GetRequiredService<IBackupOrchestrator>();

        using var source = new TempDir();
        using var destination = new TempDir();
        using var restored = new TempDir();

        _ = source.WriteText("unchanged.txt", "stays the same");
        _ = source.WriteText("changing.txt", "original content");
        _ = source.WriteText(Path.Combine("dir", "keep.txt"), "nested keep");

        var createResult = await orchestrator.ExecuteAsync(
            NewRequest(source.Path, destination.Path, BackupOperation.Create),
            new RecordingProgress<BackupStatus>()
        );
        using (Assert.EnterMultipleScope())
        {
            Assert.That(createResult.IsSuccess && createResult.Value.IsSuccess, Is.True);
            Assert.That(ChunkFiles(destination.Path), Has.Length.EqualTo(3));
        }

        const string ModifiedContent = "MODIFIED content that is clearly different from the original";
        _ = source.WriteText("changing.txt", ModifiedContent);
        const string AddedContent = "freshly added file";
        _ = source.WriteText(Path.Combine("dir", "added.txt"), AddedContent);

        var updateResult = await orchestrator.ExecuteAsync(
            NewRequest(source.Path, destination.Path, BackupOperation.Update),
            new RecordingProgress<BackupStatus>()
        );
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                    updateResult.IsSuccess && updateResult.Value.IsSuccess,
                    Is.True,
                    "Update did not succeed."
                );

            Assert.That(
                updateResult.Value.TotalFiles,
                Is.EqualTo(2),
                "Only the modified and the added file may be re-chunked; the two untouched files are carried "
                    + "over from the previous manifest without being read back through the chunking pipeline."
            );
            Assert.That(updateResult.Value.ProcessedFiles, Is.EqualTo(updateResult.Value.TotalFiles));

            Assert.That(
                ChunkFiles(destination.Path),
                Has.Length.EqualTo(4),
                "The chunk the modified file no longer references was not pruned. Four live contents remain "
                    + "(two untouched, one rewritten, one new), so the superseded chunk of changing.txt is the "
                    + "only one pruning may reclaim."
            );
        }

        var restoreResult = await orchestrator.ExecuteAsync(
            NewRequest(destination.Path, restored.Path, BackupOperation.Restore),
            new RecordingProgress<BackupStatus>()
        );
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                    restoreResult.IsSuccess && restoreResult.Value.IsSuccess,
                    Is.True,
                    "Restore of the updated backup did not succeed."
                );

            Assert.That(
                await File.ReadAllTextAsync(Path.Combine(restored.Path, "unchanged.txt")),
                Is.EqualTo("stays the same")
            );
            Assert.That(
                await File.ReadAllTextAsync(Path.Combine(restored.Path, "changing.txt")),
                Is.EqualTo(ModifiedContent)
            );
            Assert.That(
                await File.ReadAllTextAsync(Path.Combine(restored.Path, "dir", "keep.txt")),
                Is.EqualTo("nested keep")
            );
            Assert.That(
                await File.ReadAllTextAsync(Path.Combine(restored.Path, "dir", "added.txt")),
                Is.EqualTo(AddedContent)
            );
        }
    }

    [Test]
    public async Task Update_SourceFileDeleted_DropsItsEntryAndPrunesOnlyTheChunksNothingElseUses()
    {
        await using var provider = TestHost.CreateProvider();
        var orchestrator = provider.GetRequiredService<IBackupOrchestrator>();

        using var source = new TempDir();
        using var destination = new TempDir();
        using var restored = new TempDir();

        var uniquePath = source.WriteText("unique.txt", "unique content");
        var twinPath = source.WriteText("twin-a.txt", SharedContent);
        var survivorRelativePath = Path.Combine("dir", "twin-b.txt");
        _ = source.WriteText(survivorRelativePath, SharedContent);

        var createResult = await orchestrator.ExecuteAsync(
            NewRequest(source.Path, destination.Path, BackupOperation.Create),
            new RecordingProgress<BackupStatus>()
        );
        using (Assert.EnterMultipleScope())
        {
            Assert.That(createResult.IsSuccess && createResult.Value.IsSuccess, Is.True);
            Assert.That(createResult.Value.TotalFiles, Is.EqualTo(3));
            Assert.That(
                ChunkFiles(destination.Path),
                Has.Length.EqualTo(2),
                "The two byte-identical files should have been stored as a single shared chunk."
            );
        }

        File.Delete(uniquePath);
        File.Delete(twinPath);

        var updateResult = await orchestrator.ExecuteAsync(
            NewRequest(source.Path, destination.Path, BackupOperation.Update),
            new RecordingProgress<BackupStatus>()
        );
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updateResult.IsSuccess, Is.True);
            Assert.That(updateResult.Value.IsSuccess, Is.True, "Update did not succeed.");

            Assert.That(
                updateResult.Value.TotalFiles,
                Is.Zero,
                "The only surviving file is unchanged, so nothing at all has to be re-chunked."
            );

            Assert.That(
                ChunkFiles(destination.Path),
                Has.Length.EqualTo(1),
                "Pruning reclaimed the wrong number of chunks after a source file was deleted. The deleted "
                    + "unique file releases its chunk, but the shared chunk was introduced by the deleted twin "
                    + "and is still referenced by the survivor, so pruning must leave it alone."
            );
        }

        var restoreResult = await orchestrator.ExecuteAsync(
            NewRequest(destination.Path, restored.Path, BackupOperation.Restore),
            new RecordingProgress<BackupStatus>()
        );
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                restoreResult.IsSuccess && restoreResult.Value.IsSuccess,
                Is.True,
                "Restore after the pruning update did not succeed."
            );

            Assert.That(restoreResult.Value.TotalFiles, Is.EqualTo(1), "The deleted entries were not dropped.");
            Assert.That(
                Directory.GetFiles(restored.Path, "*", SearchOption.AllDirectories),
                Has.Length.EqualTo(1)
            );
            Assert.That(
                await File.ReadAllTextAsync(Path.Combine(restored.Path, survivorRelativePath)),
                Is.EqualTo(SharedContent),
                "The survivor's shared chunk was destroyed by pruning the file that introduced it."
            );
        }
    }

    /// <summary>
    /// Lists the encrypted chunk files stored under a backup root.
    /// </summary>
    /// <param name="backupRoot">The directory a backup was written to.</param>
    /// <returns>The absolute paths of the stored chunk files.</returns>
    private static string[] ChunkFiles(string backupRoot)
    {
        return Directory.GetFiles(
            Path.Combine(backupRoot, BackupConstants.ChunksDirectoryName),
            "*" + BackupConstants.AppFileExtension
        );
    }

    /// <summary>
    /// Builds an AES, PBKDF2, and Zstd request that proceeds past advisory warnings, so create,
    /// update, and restore all run against identical cryptographic options.
    /// </summary>
    /// <param name="sourcePath">The tree to back up, or the backup directory to restore from.</param>
    /// <param name="destinationPath">The directory the backup or the restored files are written to.</param>
    /// <param name="operation">The operation to dispatch.</param>
    /// <returns>The assembled request.</returns>
    private static BackupRequest NewRequest(
        string sourcePath,
        string destinationPath,
        BackupOperation operation
    )
    {
        return new BackupRequest(
            sourcePath,
            destinationPath,
            Password,
            Password,
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.PBKDF2,
            operation,
            CompressionMode.Zstd,
            ProceedOnWarnings: true
        );
    }
}
