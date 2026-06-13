using BackupZCrypt.Application.Orchestrators.Interfaces;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.ValueObjects.Backup;
using BackupZCrypt.Test.Common;
using Microsoft.Extensions.DependencyInjection;

namespace BackupZCrypt.Test.Integration;

// An Update backup re-chunks only changed/new files and re-points the manifest. After
// updating, a Restore must reflect the modified content and the newly added file.
public sealed class UpdateBackupTests
{
    private const string Password = "Correct-Horse-Battery-Staple-42";

    [Fact]
    public async Task Update_ThenRestore_ReflectsModifiedAndAddedFiles()
    {
        using var provider = TestHost.CreateProvider();
        var orchestrator = provider.GetRequiredService<IBackupOrchestrator>();

        using var source = new TempDir();
        using var destination = new TempDir();
        using var restored = new TempDir();

        source.WriteText("unchanged.txt", "stays the same");
        source.WriteText("changing.txt", "original content");
        source.WriteText(Path.Combine("dir", "keep.txt"), "nested keep");

        // --- Create ---
        var createResult = await orchestrator.ExecuteAsync(
            NewRequest(source.Path, destination.Path, BackupOperation.Create),
            new RecordingProgress<BackupStatus>()
        );
        Assert.True(createResult.IsSuccess && createResult.Value.IsSuccess);

        // --- Mutate the source: modify one file, add a brand-new one ---
        var modifiedContent = "MODIFIED content that is clearly different from the original";
        source.WriteText("changing.txt", modifiedContent);
        var addedContent = "freshly added file";
        source.WriteText(Path.Combine("dir", "added.txt"), addedContent);

        // --- Update --- (same source dir, destination must already exist)
        var updateResult = await orchestrator.ExecuteAsync(
            NewRequest(source.Path, destination.Path, BackupOperation.Update),
            new RecordingProgress<BackupStatus>()
        );
        Assert.True(
            updateResult.IsSuccess && updateResult.Value.IsSuccess,
            "Update did not succeed."
        );

        // Update reports only the files it had to (re)process: the changed one + the new one.
        Assert.Equal(2, updateResult.Value.TotalFiles);
        Assert.Equal(updateResult.Value.TotalFiles, updateResult.Value.ProcessedFiles);

        // --- Restore the updated backup ---
        var restoreResult = await orchestrator.ExecuteAsync(
            NewRequest(destination.Path, restored.Path, BackupOperation.Restore),
            new RecordingProgress<BackupStatus>()
        );
        Assert.True(
            restoreResult.IsSuccess && restoreResult.Value.IsSuccess,
            "Restore of the updated backup did not succeed."
        );

        // All four files (unchanged, modified, kept-nested, added-nested) are present and
        // carry the post-update content.
        Assert.Equal(
            "stays the same",
            File.ReadAllText(Path.Combine(restored.Path, "unchanged.txt"))
        );
        Assert.Equal(
            modifiedContent,
            File.ReadAllText(Path.Combine(restored.Path, "changing.txt"))
        );
        Assert.Equal(
            "nested keep",
            File.ReadAllText(Path.Combine(restored.Path, "dir", "keep.txt"))
        );
        Assert.Equal(
            addedContent,
            File.ReadAllText(Path.Combine(restored.Path, "dir", "added.txt"))
        );
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
