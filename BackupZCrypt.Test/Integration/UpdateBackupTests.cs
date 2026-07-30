using BackupZCrypt.Application.Orchestrators.Interfaces;
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
        Assert.That(createResult.IsSuccess && createResult.Value.IsSuccess, Is.True);

        const string modifiedContent = "MODIFIED content that is clearly different from the original";
        _ = source.WriteText("changing.txt", modifiedContent);
        const string addedContent = "freshly added file";
        _ = source.WriteText(Path.Combine("dir", "added.txt"), addedContent);

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

            Assert.That(updateResult.Value.TotalFiles, Is.EqualTo(2));
            Assert.That(updateResult.Value.ProcessedFiles, Is.EqualTo(updateResult.Value.TotalFiles));
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
                Is.EqualTo(modifiedContent)
            );
            Assert.That(
                await File.ReadAllTextAsync(Path.Combine(restored.Path, "dir", "keep.txt")),
                Is.EqualTo("nested keep")
            );
            Assert.That(
                await File.ReadAllTextAsync(Path.Combine(restored.Path, "dir", "added.txt")),
                Is.EqualTo(addedContent)
            );
        }
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
