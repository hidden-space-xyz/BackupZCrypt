using BackupZCrypt.Application.Orchestrators.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.ValueObjects.Backup;
using BackupZCrypt.Domain.ValueObjects.Localization;
using BackupZCrypt.Test.Common;

using Microsoft.Extensions.DependencyInjection;

namespace BackupZCrypt.Test.Integration;

public sealed class BackupRestoreRoundtripTests
{
    private const string Password = "Correct-Horse-Battery-Staple-42";

    private static IEnumerable<TestCaseData> Configs()
    {
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
        await using var provider = TestHost.CreateProvider();
        var orchestrator = provider.GetRequiredService<IBackupOrchestrator>();

        using var source = new TempDir();
        using var destination = new TempDir();
        using var restored = new TempDir();

        var expected = BuildSourceTree(source);

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

        using (Assert.EnterMultipleScope())
        {
            Assert.That(createResult.IsSuccess, Is.True, DescribeErrors("Create failed", createResult.Errors));
            Assert.That(
                createResult.Value.IsSuccess,
                Is.True,
                DescribeErrors("Create inner result failed", createResult.Value.Errors)
            );
            Assert.That(createResult.Value.TotalFiles, Is.EqualTo(expected.Count));
            Assert.That(createResult.Value.ProcessedFiles, Is.EqualTo(createResult.Value.TotalFiles));
        }

        var manifestPath = Path.Combine(destination.Path, BackupConstants.ManifestFileName);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(manifestPath), Is.True, $"Manifest not written at '{manifestPath}'.");
            Assert.That(createProgress.Reports, Is.Not.Empty);
        }

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

        using (Assert.EnterMultipleScope())
        {
            Assert.That(restoreResult.IsSuccess, Is.True, DescribeErrors("Restore failed", restoreResult.Errors));
            Assert.That(
                restoreResult.Value.IsSuccess,
                Is.True,
                DescribeErrors("Restore inner result failed", restoreResult.Value.Errors)
            );
            Assert.That(restoreResult.Value.ProcessedFiles, Is.EqualTo(expected.Count));
        }

        AssertTreesByteIdentical(expected, restored.Path);
    }

    [Test]
    public async Task Restore_WithWrongPassword_FailsAndDoesNotReproduceOriginals()
    {
        await using var provider = TestHost.CreateProvider();
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

        var failed = (!restoreResult.IsSuccess) || (!restoreResult.Value.IsSuccess);
        Assert.That(failed, Is.True, "Restore with a wrong password unexpectedly succeeded.");

        var codes = CollectCodes(restoreResult);
        Assert.That(
            codes,
            Does.Contain(MessageCode.InvalidPassword),
            "Expected InvalidPassword, got: " + string.Join(", ", codes)
        );

        foreach (var (relativePath, content) in expected)
        {
            var restoredFile = Path.Combine(restored.Path, relativePath);
            if (File.Exists(restoredFile))
            {
                Assert.That(await File.ReadAllBytesAsync(restoredFile), Is.Not.EqualTo(content));
            }
        }
    }

    [Test]
    public async Task Restore_WithoutManifest_FailsWithManifestRequiredForDecryption()
    {
        await using var provider = TestHost.CreateProvider();
        var orchestrator = provider.GetRequiredService<IBackupOrchestrator>();

        using var backup = new TempDir();
        using var restored = new TempDir();

        _ = backup.WriteText("stray.txt", "not a manifest");

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

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False, "Restore without a manifest unexpectedly succeeded.");
            Assert.That(
                result.Errors,
                Has.Some.Matches<LocalizableMessage>(e => e.Code == MessageCode.ManifestRequiredForDecryption)
            );
        }
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

    private static Dictionary<string, byte[]> BuildSourceTree(TempDir source)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        void Add(string relativePath, byte[] content)
        {
            _ = source.WriteFile(relativePath, content);
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
        foreach (var (relativePath, content) in expected)
        {
            var restoredFile = Path.Combine(restoredRoot, relativePath);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(File.Exists(restoredFile), Is.True, $"Missing restored file '{relativePath}'.");
                Assert.That(File.ReadAllBytes(restoredFile), Is.EqualTo(content));
            }
        }

        var restoredFiles = Directory
            .GetFiles(restoredRoot, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(restoredRoot, f))
            .ToHashSet(StringComparer.Ordinal);

        Assert.That(restoredFiles, Has.Count.EqualTo(expected.Count));
    }

    private static HashSet<MessageCode> CollectCodes(Result<BackupResult> result)
    {
        var codes = result.Errors.Select(e => e.Code).ToHashSet();
        if (result.IsSuccess)
        {
            foreach (var error in result.Value.Errors)
            {
                _ = codes.Add(error.Code);
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
