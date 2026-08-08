using BackupZCrypt.Application.Commands;
using BackupZCrypt.Application.Commands.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Application.ValueObjects.Backup;
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
        var createHandler = provider.GetRequiredService<ICommandHandler<CreateBackupCommand, Result<BackupOutcome>>>();
        var updateHandler = provider.GetRequiredService<ICommandHandler<UpdateBackupCommand, Result<BackupOutcome>>>();
        var restoreHandler = provider.GetRequiredService<ICommandHandler<RestoreBackupCommand, Result<BackupOutcome>>>();

        using var source = new TempDir();
        using var destination = new TempDir();
        using var restored = new TempDir();

        _ = source.WriteText("unchanged.txt", "stays the same");
        _ = source.WriteText("changing.txt", "original content");
        _ = source.WriteText(Path.Combine("dir", "keep.txt"), "nested keep");

        var createResult = await createHandler.HandleAsync(NewCreateCommand(source.Path, destination.Path));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(createResult.IsSuccess && createResult.Value.Completion!.IsSuccess, Is.True);
            Assert.That(ChunkFiles(destination.Path), Has.Length.EqualTo(3));
        }

        const string ModifiedContent = "MODIFIED content that is clearly different from the original";
        _ = source.WriteText("changing.txt", ModifiedContent);
        const string AddedContent = "freshly added file";
        _ = source.WriteText(Path.Combine("dir", "added.txt"), AddedContent);

        var updateResult = await updateHandler.HandleAsync(NewUpdateCommand(source.Path, destination.Path));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                    updateResult.IsSuccess && updateResult.Value.Completion!.IsSuccess,
                    Is.True,
                    "Update did not succeed."
                );

            Assert.That(
                updateResult.Value.Completion!.TotalFiles,
                Is.EqualTo(2),
                "Only the modified and the added file may be re-chunked; the two untouched files are carried "
                    + "over from the previous manifest without being read back through the chunking pipeline."
            );
            Assert.That(
                updateResult.Value.Completion.ProcessedFiles,
                Is.EqualTo(updateResult.Value.Completion.TotalFiles)
            );

            Assert.That(
                ChunkFiles(destination.Path),
                Has.Length.EqualTo(4),
                "The chunk the modified file no longer references was not pruned. Four live contents remain "
                    + "(two untouched, one rewritten, one new), so the superseded chunk of changing.txt is the "
                    + "only one pruning may reclaim."
            );
        }

        var restoreResult = await restoreHandler.HandleAsync(NewRestoreCommand(destination.Path, restored.Path));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                    restoreResult.IsSuccess && restoreResult.Value.Completion!.IsSuccess,
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
        var createHandler = provider.GetRequiredService<ICommandHandler<CreateBackupCommand, Result<BackupOutcome>>>();
        var updateHandler = provider.GetRequiredService<ICommandHandler<UpdateBackupCommand, Result<BackupOutcome>>>();
        var restoreHandler = provider.GetRequiredService<ICommandHandler<RestoreBackupCommand, Result<BackupOutcome>>>();

        using var source = new TempDir();
        using var destination = new TempDir();
        using var restored = new TempDir();

        var uniquePath = source.WriteText("unique.txt", "unique content");
        var twinPath = source.WriteText("twin-a.txt", SharedContent);
        var survivorRelativePath = Path.Combine("dir", "twin-b.txt");
        _ = source.WriteText(survivorRelativePath, SharedContent);

        var createResult = await createHandler.HandleAsync(NewCreateCommand(source.Path, destination.Path));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(createResult.IsSuccess && createResult.Value.Completion!.IsSuccess, Is.True);
            Assert.That(createResult.Value.Completion!.TotalFiles, Is.EqualTo(3));
            Assert.That(
                ChunkFiles(destination.Path),
                Has.Length.EqualTo(2),
                "The two byte-identical files should have been stored as a single shared chunk."
            );
        }

        File.Delete(uniquePath);
        File.Delete(twinPath);

        var updateResult = await updateHandler.HandleAsync(NewUpdateCommand(source.Path, destination.Path));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updateResult.IsSuccess, Is.True);
            Assert.That(updateResult.Value.Completion!.IsSuccess, Is.True, "Update did not succeed.");

            Assert.That(
                updateResult.Value.Completion.TotalFiles,
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

        var restoreResult = await restoreHandler.HandleAsync(NewRestoreCommand(destination.Path, restored.Path));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                restoreResult.IsSuccess && restoreResult.Value.Completion!.IsSuccess,
                Is.True,
                "Restore after the pruning update did not succeed."
            );

            Assert.That(
                restoreResult.Value.Completion!.TotalFiles,
                Is.EqualTo(1),
                "The deleted entries were not dropped."
            );
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
    /// Builds an AES, PBKDF2, and Zstd create command that proceeds past advisory warnings and
    /// records progress into a fresh sink.
    /// </summary>
    /// <param name="sourcePath">The tree to back up.</param>
    /// <param name="destinationPath">The directory the backup is written to.</param>
    /// <returns>The assembled command.</returns>
    private static CreateBackupCommand NewCreateCommand(string sourcePath, string destinationPath)
    {
        return new CreateBackupCommand(
            sourcePath,
            destinationPath,
            Password,
            Password,
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.PBKDF2,
            CompressionMode.Zstd,
            ProceedOnWarnings: true
        )
        {
            Progress = new RecordingProgress<BackupStatus>(),
        };
    }

    /// <summary>
    /// Builds an update command that proceeds past advisory warnings and records progress into a
    /// fresh sink; the archive's own algorithms govern the run.
    /// </summary>
    /// <param name="sourcePath">The tree whose current contents feed the update.</param>
    /// <param name="backupPath">The existing backup directory to update.</param>
    /// <returns>The assembled command.</returns>
    private static UpdateBackupCommand NewUpdateCommand(string sourcePath, string backupPath)
    {
        return new UpdateBackupCommand(sourcePath, backupPath, Password, ProceedOnWarnings: true)
        {
            Progress = new RecordingProgress<BackupStatus>(),
        };
    }

    /// <summary>
    /// Builds a restore command that proceeds past advisory warnings and records progress into a
    /// fresh sink; the archive's own algorithms govern the run.
    /// </summary>
    /// <param name="backupPath">The backup directory to restore from.</param>
    /// <param name="destinationPath">The directory the restored files are written to.</param>
    /// <returns>The assembled command.</returns>
    private static RestoreBackupCommand NewRestoreCommand(string backupPath, string destinationPath)
    {
        return new RestoreBackupCommand(backupPath, destinationPath, Password, ProceedOnWarnings: true)
        {
            Progress = new RecordingProgress<BackupStatus>(),
        };
    }
}
