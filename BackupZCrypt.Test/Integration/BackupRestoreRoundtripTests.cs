using BackupZCrypt.Application.Orchestrators.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.ValueObjects.Backup;
using BackupZCrypt.Domain.ValueObjects.Localization;
using BackupZCrypt.Test.Common;

using Microsoft.Extensions.DependencyInjection;

namespace BackupZCrypt.Test.Integration;

/// <summary>
/// Integration tests covering the full backup-then-restore round trip across algorithm combinations.
/// </summary>
public sealed class BackupRestoreRoundtripTests
{
    /// <summary>
    /// The password every backup in this fixture is created and restored with; long and varied
    /// enough to clear the validator's strength warnings.
    /// </summary>
    private const string Password = "Correct-Horse-Battery-Staple-42";

    /// <summary>
    /// Supplies the algorithm combinations exercised by the round-trip test.
    /// </summary>
    /// <returns>One case per encryption, compression, and key-derivation combination under test.</returns>
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

    /// <summary>
    /// Builds a request that uses the fixture password and proceeds past advisory warnings.
    /// </summary>
    /// <param name="sourcePath">The tree to back up, or the backup directory to read from.</param>
    /// <param name="destinationPath">The directory the backup or the restored files are written to.</param>
    /// <param name="encryption">The AEAD cipher to protect chunks and the manifest with.</param>
    /// <param name="compression">The compression mode applied to chunks before encryption.</param>
    /// <param name="keyDerivation">The key derivation function used to derive the master key.</param>
    /// <param name="operation">The operation to dispatch.</param>
    /// <returns>The assembled request.</returns>
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

    /// <summary>
    /// Populates the source directory with the tree the round trip must reproduce: nested folders,
    /// text, an empty file, a 37-byte file, and a 512 KiB binary payload.
    /// </summary>
    /// <param name="source">The temporary directory to write the tree into.</param>
    /// <returns>The written content keyed by path relative to <paramref name="source"/>.</returns>
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

    /// <summary>
    /// Produces pseudo-random but reproducible bytes, so a failure can be replayed exactly.
    /// </summary>
    /// <param name="length">The number of bytes to generate.</param>
    /// <param name="seed">The seed fixing the byte sequence.</param>
    /// <returns>The generated bytes.</returns>
    private static byte[] DeterministicBytes(int length, int seed)
    {
        var data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }

    /// <summary>
    /// Asserts that every expected file was restored byte for byte and that nothing extra appeared
    /// under the restore root.
    /// </summary>
    /// <param name="expected">The original content keyed by relative path.</param>
    /// <param name="restoredRoot">The directory the backup was restored into.</param>
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

    /// <summary>
    /// Gathers the orchestrator's own error codes together with the per-file codes of the inner
    /// backup result, which is only read when the outer result succeeded because
    /// <see cref="BackupZCrypt.Application.ValueObjects.Result{T}.Value"/> throws on a failure.
    /// </summary>
    /// <param name="result">The orchestrator outcome to inspect.</param>
    /// <returns>The distinct message codes reported at either level.</returns>
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

    /// <summary>
    /// Renders an assertion message that names the failing step and the codes behind it.
    /// </summary>
    /// <param name="prefix">The text describing which step failed.</param>
    /// <param name="errors">The errors to append; omitted when the list is empty.</param>
    /// <returns>The assertion message.</returns>
    private static string DescribeErrors(string prefix, IReadOnlyList<LocalizableMessage> errors)
    {
        return errors.Count == 0
            ? prefix
            : $"{prefix}: {string.Join(", ", errors.Select(e => e.Code))}";
    }
}
