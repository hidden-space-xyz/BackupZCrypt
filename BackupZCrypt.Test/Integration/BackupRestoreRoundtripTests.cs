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
    public static IEnumerable<TestCaseData> Configs()
    {
        yield return new TestCaseData(
            EncryptionAlgorithm.None,
            CompressionMode.None,
            KeyDerivationAlgorithm.PBKDF2
        );
        yield return new TestCaseData(
            EncryptionAlgorithm.Aes,
            CompressionMode.Zstd,
            KeyDerivationAlgorithm.PBKDF2
        );
        yield return new TestCaseData(
            EncryptionAlgorithm.Aes,
            CompressionMode.None,
            KeyDerivationAlgorithm.Argon2id
        );
    }

    [TestCaseSource(nameof(Configs))]
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

        Assert.That(createResult.IsSuccess, Is.True, DescribeErrors("Create failed", createResult.Errors));
        Assert.That(
            createResult.Value.IsSuccess,
            Is.True,
            DescribeErrors("Create inner result failed", createResult.Value.Errors)
        );
        Assert.That(createResult.Value.TotalFiles, Is.EqualTo(expected.Count));
        Assert.That(createResult.Value.ProcessedFiles, Is.EqualTo(createResult.Value.TotalFiles));

        var manifestPath = Path.Combine(destination.Path, BackupConstants.ManifestFileName);
        Assert.That(File.Exists(manifestPath), Is.True, $"Manifest not written at '{manifestPath}'.");
        Assert.That(createProgress.Reports, Is.Not.Empty);

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

        Assert.That(restoreResult.IsSuccess, Is.True, DescribeErrors("Restore failed", restoreResult.Errors));
        Assert.That(
            restoreResult.Value.IsSuccess,
            Is.True,
            DescribeErrors("Restore inner result failed", restoreResult.Value.Errors)
        );
        Assert.That(restoreResult.Value.ProcessedFiles, Is.EqualTo(expected.Count));

        AssertTreesByteIdentical(expected, restored.Path);
    }

    [Test]
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
        Assert.That(createResult.IsSuccess && createResult.Value.IsSuccess, Is.True);

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
        Assert.That(failed, Is.True, "Restore with a wrong password unexpectedly succeeded.");

        var codes = CollectCodes(restoreResult);
        Assert.That(
            codes.Contains(MessageCode.ManifestRequiredForDecryption)
                || codes.Contains(MessageCode.InvalidPassword),
            Is.True,
            $"Expected ManifestRequiredForDecryption or InvalidPassword, got: "
                + string.Join(", ", codes)
        );

        // Nothing usable must have been written: no restored file may equal its original.
        foreach (var (relativePath, content) in expected)
        {
            var restoredFile = Path.Combine(restored.Path, relativePath);
            if (File.Exists(restoredFile))
            {
                Assert.That(File.ReadAllBytes(restoredFile), Is.Not.EqualTo(content));
            }
        }
    }

    [Test]
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

        Assert.That(result.IsSuccess, Is.False, "Restore without a manifest unexpectedly succeeded.");
        Assert.That(
            result.Errors,
            Has.Some.Matches<LocalizableMessage>(e => e.Code == MessageCode.ManifestRequiredForDecryption)
        );
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
            Assert.That(File.Exists(restoredFile), Is.True, $"Missing restored file '{relativePath}'.");
            Assert.That(File.ReadAllBytes(restoredFile), Is.EqualTo(content));
        }

        // And the restore introduced no extra files beyond what was backed up.
        var restoredFiles = Directory
            .GetFiles(restoredRoot, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(restoredRoot, f))
            .ToHashSet(StringComparer.Ordinal);

        Assert.That(restoredFiles.Count, Is.EqualTo(expected.Count));
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
