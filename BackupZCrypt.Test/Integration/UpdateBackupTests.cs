using BackupZCrypt.Application.Orchestrators.Interfaces;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.ValueObjects.Backup;
using BackupZCrypt.Test.Common;
using Microsoft.Extensions.DependencyInjection;

namespace BackupZCrypt.Test.Integration;

public sealed class UpdateBackupTests
{
    private const string Password = "Correct-Horse-Battery-Staple-42";

    [Test]
    public async Task Update_ThenRestore_ReflectsModifiedAndAddedFiles()
    {
        await using var provider = TestHost.CreateProvider();
        var orchestrator = provider.GetRequiredService<IBackupOrchestrator>();

        using var source = new TempDir();
        using var destination = new TempDir();
        using var restored = new TempDir();

        source.WriteText("unchanged.txt", "stays the same");
        source.WriteText("changing.txt", "original content");
        source.WriteText(Path.Combine("dir", "keep.txt"), "nested keep");

        var createResult = await orchestrator.ExecuteAsync(
            NewRequest(source.Path, destination.Path, BackupOperation.Create),
            new RecordingProgress<BackupStatus>()
        );
        Assert.That(createResult.IsSuccess && createResult.Value.IsSuccess, Is.True);

        const string modifiedContent = "MODIFIED content that is clearly different from the original";
        source.WriteText("changing.txt", modifiedContent);
        const string addedContent = "freshly added file";
        source.WriteText(Path.Combine("dir", "added.txt"), addedContent);

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
