using BackupZCrypt.Application.Orchestrators.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.ValueObjects.Backup;
using BackupZCrypt.Domain.ValueObjects.Localization;
using BackupZCrypt.Test.Common;
using Microsoft.Extensions.DependencyInjection;

namespace BackupZCrypt.Test.Integration;

// End-to-end coverage of the real wired pipeline (DI + real file system + real crypto):
// a Create backup followed by a Restore must reproduce every source file byte-for-byte.
// Drives the orchestrator exactly the way the desktop app does (by BackupOperation).
public sealed class BackupRestoreRoundtripTests
{
    private const string Password = "Correct-Horse-Battery-Staple-42";

    // PBKDF2 for the cheap cases, exactly one Argon2id case (it allocates ~256 MB and is
    // intentionally slow). Covers: plain copy, AES+Zstd, and a single Argon2id run.
    public static TheoryData<EncryptionAlgorithm, CompressionMode, KeyDerivationAlgorithm> Configs() =>
        new()
        {
            { EncryptionAlgorithm.None, CompressionMode.None, KeyDerivationAlgorithm.PBKDF2 },
            { EncryptionAlgorithm.Aes, CompressionMode.Zstd, KeyDerivationAlgorithm.PBKDF2 },
            { EncryptionAlgorithm.Aes, CompressionMode.None, KeyDerivationAlgorithm.Argon2id },
        };

    [Theory]
    [MemberData(nameof(Configs))]
    public async Task CreateThenRestore_ReproducesEverySourceFile(
        EncryptionAlgorithm encryption,
        CompressionMode compression,
        KeyDerivationAlgorithm keyDerivation
    )
    {
        using var provider = TestHost.CreateProvider();
        var orchestrator = provider.GetRequiredService<IBackupOrchestrator>();

        using var source = new TempDir();
        using var destination = new TempDir();
        using var restored = new TempDir();

        var expected = BuildSourceTree(source);

        // --- Create ---
        var createProgress = new RecordingProgress<BackupStatus>();
        var createRequest = NewRequest(
            source.Path,
            destination.Path,
            encryption,
            compression,
            keyDerivation,
            BackupOperation.Create
        );

        var createResult = await orchestrator.ExecuteAsync(createRequest, createProgress);

        Assert.True(createResult.IsSuccess, DescribeErrors("Create failed", createResult.Errors));
        Assert.True(
            createResult.Value.IsSuccess,
            DescribeErrors("Create inner result failed", createResult.Value.Errors)
        );
        Assert.Equal(expected.Count, createResult.Value.TotalFiles);
        Assert.Equal(createResult.Value.TotalFiles, createResult.Value.ProcessedFiles);

        var manifestPath = Path.Combine(destination.Path, BackupConstants.ManifestFileName);
        Assert.True(File.Exists(manifestPath), $"Manifest not written at '{manifestPath}'.");
        Assert.NotEmpty(createProgress.Reports);

        // --- Restore --- (the backup directory is the source of a Restore operation)
        var restoreProgress = new RecordingProgress<BackupStatus>();
        var restoreRequest = NewRequest(
            destination.Path,
            restored.Path,
            encryption,
            compression,
            keyDerivation,
            BackupOperation.Restore
        );

        var restoreResult = await orchestrator.ExecuteAsync(restoreRequest, restoreProgress);

        Assert.True(restoreResult.IsSuccess, DescribeErrors("Restore failed", restoreResult.Errors));
        Assert.True(
            restoreResult.Value.IsSuccess,
            DescribeErrors("Restore inner result failed", restoreResult.Value.Errors)
        );
        Assert.Equal(expected.Count, restoreResult.Value.ProcessedFiles);

        AssertTreesByteIdentical(expected, restored.Path);
    }

    [Fact]
    public async Task Restore_WithWrongPassword_FailsAndDoesNotReproduceOriginals()
    {
        using var provider = TestHost.CreateProvider();
        var orchestrator = provider.GetRequiredService<IBackupOrchestrator>();

        using var source = new TempDir();
        using var destination = new TempDir();
        using var restored = new TempDir();

        var expected = BuildSourceTree(source);

        var createResult = await orchestrator.ExecuteAsync(
            NewRequest(
                source.Path,
                destination.Path,
                EncryptionAlgorithm.Aes,
                CompressionMode.None,
                KeyDerivationAlgorithm.PBKDF2,
                BackupOperation.Create
            ),
            new RecordingProgress<BackupStatus>()
        );
        Assert.True(createResult.IsSuccess && createResult.Value.IsSuccess);

        var wrongPasswordRequest = NewRequest(
            destination.Path,
            restored.Path,
            EncryptionAlgorithm.Aes,
            CompressionMode.None,
            KeyDerivationAlgorithm.PBKDF2,
            BackupOperation.Restore
        ) with
        {
            Password = "totally-different-password",
            ConfirmPassword = "totally-different-password",
        };

        var restoreResult = await orchestrator.ExecuteAsync(
            wrongPasswordRequest,
            new RecordingProgress<BackupStatus>()
        );

        // A wrong password fails AEAD verification of the manifest first, so the pipeline
        // reports ManifestRequiredForDecryption (chunk-level InvalidPassword is only reached
        // when the manifest happens to decrypt). Accept either: the contract is "did not
        // succeed and surfaced a crypto-related error".
        var failed = (!restoreResult.IsSuccess) || (!restoreResult.Value.IsSuccess);
        Assert.True(failed, "Restore with a wrong password unexpectedly succeeded.");

        var codes = CollectCodes(restoreResult);
        Assert.True(
            codes.Contains(MessageCode.ManifestRequiredForDecryption)
                || codes.Contains(MessageCode.InvalidPassword),
            $"Expected ManifestRequiredForDecryption or InvalidPassword, got: "
                + string.Join(", ", codes)
        );

        // Nothing usable must have been written: no restored file may equal its original.
        foreach (var (relativePath, content) in expected)
        {
            var restoredFile = Path.Combine(restored.Path, relativePath);
            if (File.Exists(restoredFile))
            {
                Assert.NotEqual(content, File.ReadAllBytes(restoredFile));
            }
        }
    }

    [Fact]
    public async Task Restore_WithoutManifest_FailsWithManifestRequiredForDecryption()
    {
        using var provider = TestHost.CreateProvider();
        var orchestrator = provider.GetRequiredService<IBackupOrchestrator>();

        using var backup = new TempDir();
        using var restored = new TempDir();

        // A directory that exists but contains no manifest.bzc. Add a stray file so the
        // request passes source-exists validation but restore still cannot find a manifest.
        backup.WriteText("stray.txt", "not a manifest");

        var result = await orchestrator.ExecuteAsync(
            NewRequest(
                backup.Path,
                restored.Path,
                EncryptionAlgorithm.Aes,
                CompressionMode.None,
                KeyDerivationAlgorithm.PBKDF2,
                BackupOperation.Restore
            ),
            new RecordingProgress<BackupStatus>()
        );

        Assert.False(result.IsSuccess, "Restore without a manifest unexpectedly succeeded.");
        Assert.Contains(result.Errors, e => e.Code == MessageCode.ManifestRequiredForDecryption);
    }

    private static BackupRequest NewRequest(
        string sourcePath,
        string destinationPath,
        EncryptionAlgorithm encryption,
        CompressionMode compression,
        KeyDerivationAlgorithm keyDerivation,
        BackupOperation operation
    )
    {
        return new BackupRequest(
            sourcePath,
            destinationPath,
            Password,
            Password,
            encryption,
            keyDerivation,
            operation,
            compression,
            ProceedOnWarnings: true
        );
    }

    // Builds a varied tree: small text, a larger binary blob (forces multiple chunks),
    // a nested subdirectory and an empty file. Returns relative-path -> bytes for later
    // byte-for-byte comparison.
    private static Dictionary<string, byte[]> BuildSourceTree(TempDir source)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        void Add(string relativePath, byte[] content)
        {
            source.WriteFile(relativePath, content);
            files[relativePath] = content;
        }

        Add("readme.txt", "Hello, BackupZCrypt integration test.\n"u8.ToArray());
        Add(Path.Combine("docs", "notes.md"), "# Notes\n\nNested file content.\n"u8.ToArray());
        Add(Path.Combine("docs", "sub", "deep.txt"), "Deeply nested.\n"u8.ToArray());
        Add("empty.dat", []);
        Add("binary.bin", DeterministicBytes(512 * 1024, seed: 1234));
        Add("small.bin", DeterministicBytes(37, seed: 99));

        return files;
    }

    private static byte[] DeterministicBytes(int length, int seed)
    {
        var data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }

    private static void AssertTreesByteIdentical(
        Dictionary<string, byte[]> expected,
        string restoredRoot
    )
    {
        // Every source file is reproduced at the same relative path with identical bytes.
        foreach (var (relativePath, content) in expected)
        {
            var restoredFile = Path.Combine(restoredRoot, relativePath);
            Assert.True(File.Exists(restoredFile), $"Missing restored file '{relativePath}'.");
            Assert.Equal(content, File.ReadAllBytes(restoredFile));
        }

        // And the restore introduced no extra files beyond what was backed up.
        var restoredFiles = Directory
            .GetFiles(restoredRoot, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(restoredRoot, f))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(expected.Count, restoredFiles.Count);
    }

    private static HashSet<MessageCode> CollectCodes(Result<BackupResult> result)
    {
        var codes = result.Errors.Select(e => e.Code).ToHashSet();
        if (result.IsSuccess)
        {
            foreach (var error in result.Value.Errors)
            {
                codes.Add(error.Code);
            }
        }

        return codes;
    }

    private static string DescribeErrors(string prefix, IReadOnlyList<LocalizableMessage> errors)
    {
        return errors.Count == 0
            ? prefix
            : $"{prefix}: {string.Join(", ", errors.Select(e => e.Code))}";
    }
}
