using System.Globalization;
using System.Security.Cryptography;

using BackupZCrypt.Application.Orchestrators.Interfaces;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Application.ValueObjects.Manifest;
using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Factories.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Backup;
using BackupZCrypt.Domain.ValueObjects.Localization;
using BackupZCrypt.Test.Common;

using Microsoft.Extensions.DependencyInjection;

namespace BackupZCrypt.Test.Integration;

/// <summary>
/// Integration tests pinning the security invariants of the chunked backup format: a crafted manifest
/// can never steer a write outside the restore root or past the declared byte budget, chunk file names
/// never leak the content hash, the purpose-bound sub-keys are not interchangeable, and any damage to
/// the unencrypted manifest preamble makes the backup refuse to open instead of producing wrong data.
/// </summary>
/// <remarks>
/// <para>
/// Every case is built from a real backup and, where a hostile manifest is needed, rewritten through
/// the production <see cref="IManifestService"/> so the AEAD stays valid and the engine rejects the
/// entry on its own merits rather than on a broken signature. PBKDF2 and multi-kilobyte files keep the
/// suite cheap on CI: the cases that share one expensive fixture are looped inside a single test rather
/// than repeated as separate cases, because each additional case would otherwise pay for another key
/// derivation.
/// </para>
/// <para>
/// The source files are far below the chunker's 256 KiB minimum, so each one becomes exactly a single
/// chunk whose plaintext is the whole file. That is what lets a case treat a source file's SHA-256 as
/// its chunk's content hash and expect exactly one stored chunk per file.
/// </para>
/// <para>
/// The restore roots used by the path-escape cases are nested two levels below the fixture's own
/// temporary directory, so an escape that does succeed lands inside a directory the fixture deletes
/// instead of littering the real temp folder — the assertions can prove nothing was written outside the
/// restore root without leaving anything behind when they fail.
/// </para>
/// <para>
/// The tamper cases address the manifest by its on-disk layout: byte 0 is the encryption algorithm,
/// byte 1 the key derivation, bytes 2 to 33 the master salt, bytes 34 to 45 the nonce, and the
/// ciphertext follows from byte 46. Everything before the nonce is the unencrypted preamble that also
/// serves as the AEAD associated data, so damaging any of it must make the backup refuse to open.
/// </para>
/// </remarks>
public sealed class ChunkedBackupSecurityTests
{
    /// <summary>
    /// The password every backup in this fixture is created with; long and varied enough to clear the
    /// validator's strength warnings.
    /// </summary>
    private const string Password = "Correct-Horse-Battery-Staple-42";

    /// <summary>
    /// A second, unrelated password used to prove that key material and chunk names are password
    /// dependent, and that a wrong password opens nothing.
    /// </summary>
    private const string OtherPassword = "Wrong-Horse-Battery-Staple-99";

    [Test]
    public async Task RestoreAsync_ManifestEntryEscapesDestination_RejectsEntryAndWritesNothingOutsideRestoreRoot()
    {
        await using var provider = TestHost.CreateProvider();
        var orchestrator = provider.GetRequiredService<IBackupOrchestrator>();

        using var source = new TempDir();
        using var destination = new TempDir();
        using var restoreArea = new TempDir();

        _ = source.WriteText("a.txt", "payload that must never escape the restore root");
        await CreateBackupAsync(orchestrator, source.Path, destination.Path, CompressionMode.None);

        var (preamble, masterKey, manifest) = await OpenManifestAsync(
            provider,
            destination.Path,
            Password
        );
        var manifestKey = ExpandSubKey(masterKey, "manifest-encryption"u8);
        var entry = manifest.Files[0];

        var rootsDir = Path.Combine(restoreArea.Path, "roots");

        (string Name, string EntryPath, string EscapeTarget)[] craftedCases =
        [
            (
                "forward",
                "../../escaped-forward.txt",
                Path.Combine(restoreArea.Path, "escaped-forward.txt")
            ),
            (
                "backslash",
                "..\\..\\escaped-backslash.txt",
                Path.Combine(restoreArea.Path, "escaped-backslash.txt")
            ),
            (
                "rooted",
                Path.Combine(restoreArea.Path, "escaped-rooted.txt"),
                Path.Combine(restoreArea.Path, "escaped-rooted.txt")
            ),
            (
                "sibling",
                "../sibling-evil/escaped-sibling.txt",
                Path.Combine(rootsDir, "sibling-evil", "escaped-sibling.txt")
            ),
        ];

        foreach (var (name, entryPath, escapeTarget) in craftedCases)
        {
            var caseRoot = Path.Combine(rootsDir, name);
            var crafted = manifest with { Files = [entry with { OriginalPath = entryPath }] };
            await SaveManifestAsync(provider, destination.Path, preamble, manifestKey, crafted);

            var result = await orchestrator.ExecuteAsync(
                NewRequest(destination.Path, caseRoot, BackupOperation.Restore),
                new RecordingProgress<BackupStatus>()
            );

            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    result.IsSuccess && result.Value.IsSuccess,
                    Is.False,
                    $"Restore accepted the crafted manifest entry '{entryPath}' ({name})."
                );
                Assert.That(
                    CollectCodes(result),
                    Is.Not.Empty,
                    $"The crafted entry '{entryPath}' ({name}) was rejected without reporting anything."
                );
                Assert.That(
                    File.Exists(escapeTarget),
                    Is.False,
                    $"The crafted entry '{entryPath}' ({name}) wrote to '{escapeTarget}', outside the restore root."
                );
                Assert.That(
                    FilesUnder(caseRoot),
                    Is.Empty,
                    $"The crafted entry '{entryPath}' ({name}) left files under the restore root."
                );
            }
        }
    }

    [Test]
    public async Task CreateAsync_ChunkFileNames_LeakNeitherTheContentHashNorMatchAnotherPassword()
    {
        await using var provider = TestHost.CreateProvider();
        var orchestrator = provider.GetRequiredService<IBackupOrchestrator>();

        using var source = new TempDir();
        using var firstBackup = new TempDir();
        using var secondBackup = new TempDir();

        _ = source.WriteText("alpha.txt", "alpha plaintext, one chunk");
        _ = source.WriteText("beta.txt", "beta plaintext, a different chunk");

        await CreateBackupAsync(orchestrator, source.Path, firstBackup.Path, CompressionMode.None);
        await CreateBackupAsync(
            orchestrator,
            source.Path,
            secondBackup.Path,
            CompressionMode.None,
            OtherPassword
        );

        var firstNames = ChunkFileNames(firstBackup.Path);
        var secondNames = ChunkFileNames(secondBackup.Path);

        var contentHashes = Directory
            .GetFiles(source.Path)
            .Select(f => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(f))))
            .ToArray();

        var leaking = firstNames
            .Concat(secondNames)
            .Where(n => contentHashes.Any(h => n.Contains(h, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstNames, Has.Length.EqualTo(2), "Expected one chunk per source file.");
            Assert.That(secondNames, Has.Length.EqualTo(2), "Expected one chunk per source file.");
            Assert.That(
                leaking,
                Is.Empty,
                "A chunk file name embeds the SHA-256 of its plaintext, so the chunks directory reveals its contents."
            );
            Assert.That(
                firstNames.Intersect(secondNames, StringComparer.OrdinalIgnoreCase),
                Is.Empty,
                "Two backups of identical content under different passwords produced the same chunk file names."
            );
        }
    }

    [Test]
    public async Task DerivedSubKeys_BoundToDifferentPurposes_AreDistinctAndNotInterchangeable()
    {
        await using var provider = TestHost.CreateProvider();
        var orchestrator = provider.GetRequiredService<IBackupOrchestrator>();
        var manifestService = provider.GetRequiredService<IManifestService>();

        using var source = new TempDir();
        using var destination = new TempDir();

        _ = source.WriteText("only.txt", "sub-key separation probe");
        await CreateBackupAsync(orchestrator, source.Path, destination.Path, CompressionMode.None);

        var (preamble, masterKey, manifest) = await OpenManifestAsync(
            provider,
            destination.Path,
            Password
        );

        var chunkEncryptionKey = ExpandSubKey(masterKey, "chunk-encryption"u8);
        var chunkNonceKey = ExpandSubKey(masterKey, "chunk-nonce"u8);
        var namingKey = ExpandSubKey(masterKey, "chunk-naming"u8);
        var manifestKey = ExpandSubKey(masterKey, "manifest-encryption"u8);

        var chunkRef = manifest.Files[0].Chunks[0];
        var chunkHash = Convert.FromBase64String(chunkRef.Hash);
        var namingMac = HMACSHA256.HashData(namingKey, chunkHash);
        var nonceMac = HMACSHA256.HashData(chunkNonceKey, chunkHash);

        var distinctSubKeys = new[] { chunkEncryptionKey, chunkNonceKey, namingKey, manifestKey }
            .Select(k => Convert.ToHexStringLower(k))
            .ToHashSet(StringComparer.Ordinal);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                ChunkFileNames(destination.Path).Single(),
                Is.EqualTo(Convert.ToHexStringLower(namingMac) + BackupConstants.AppFileExtension),
                "The chunk file name is no longer HMAC-SHA256(chunk-naming sub-key, chunk hash)."
            );
            Assert.That(
                Convert.FromBase64String(chunkRef.Nonce),
                Is.EqualTo(nonceMac[..EncryptionConstants.NonceSize]),
                "The recorded chunk nonce is no longer HMAC-SHA256(chunk-nonce sub-key, chunk hash) truncated."
            );

            Assert.That(
                namingMac,
                Is.Not.EqualTo(nonceMac),
                "The chunk-naming and chunk-nonce sub-keys produced the same HMAC, so a file name leaks its nonce."
            );
            Assert.That(
                distinctSubKeys,
                Has.Count.EqualTo(4),
                "The four HKDF context labels did not produce four distinct sub-keys."
            );

            Assert.That(
                manifestService.DecryptChunkManifest(preamble, manifestKey),
                Is.Not.Null,
                "The manifest did not open under the manifest-encryption sub-key."
            );
            Assert.That(
                manifestService.DecryptChunkManifest(preamble, chunkEncryptionKey),
                Is.Null,
                "The manifest opened under the chunk-encryption sub-key."
            );
            Assert.That(
                manifestService.DecryptChunkManifest(preamble, chunkNonceKey),
                Is.Null,
                "The manifest opened under the chunk-nonce sub-key."
            );
            Assert.That(
                manifestService.DecryptChunkManifest(preamble, namingKey),
                Is.Null,
                "The manifest opened under the chunk-naming sub-key."
            );
        }
    }

    [Test]
    public async Task RestoreAsync_ManifestPreambleOrPasswordTampered_FailsClosedAndRestoresNothing()
    {
        await using var provider = TestHost.CreateProvider();
        var orchestrator = provider.GetRequiredService<IBackupOrchestrator>();

        using var source = new TempDir();
        using var destination = new TempDir();
        using var restoreArea = new TempDir();

        _ = source.WriteText("a.txt", "preamble tamper probe alpha");
        _ = source.WriteText("b.txt", "preamble tamper probe beta");
        await CreateBackupAsync(orchestrator, source.Path, destination.Path, CompressionMode.None);

        var manifestPath = Path.Combine(destination.Path, BackupConstants.ManifestFileName);
        var pristine = await File.ReadAllBytesAsync(manifestPath);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pristine, Has.Length.GreaterThan(60), "The manifest is too short to tamper with.");
            Assert.That(pristine[0], Is.EqualTo((byte)EncryptionAlgorithm.Aes));
            Assert.That(pristine[1], Is.EqualTo((byte)KeyDerivationAlgorithm.PBKDF2));
        }

        (string Name, int Offset, byte Value, string CasePassword, MessageCode Expected)[] tamperCases =
        [
            (
                "algorithm byte downgraded to another cipher",
                0,
                (byte)EncryptionAlgorithm.Twofish,
                Password,
                MessageCode.InvalidPassword
            ),
            (
                "key derivation byte set to an undefined value",
                1,
                0x7F,
                Password,
                MessageCode.ManifestRequiredForDecryption
            ),
            ("master salt bit-flipped", 5, (byte)(pristine[5] ^ 0xFF), Password, MessageCode.InvalidPassword),
            ("manifest nonce bit-flipped", 40, (byte)(pristine[40] ^ 0xFF), Password, MessageCode.InvalidPassword),
            ("ciphertext bit-flipped", 50, (byte)(pristine[50] ^ 0xFF), Password, MessageCode.InvalidPassword),
            ("manifest intact but the password is wrong", -1, 0, OtherPassword, MessageCode.InvalidPassword),
        ];

        var caseIndex = 0;

        foreach (var (name, offset, value, casePassword, expected) in tamperCases)
        {
            var tampered = pristine.ToArray();
            if (offset >= 0)
            {
                tampered[offset] = value;
            }

            await File.WriteAllBytesAsync(manifestPath, tampered);

            var caseRoot = Path.Combine(
                restoreArea.Path,
                "case" + caseIndex.ToString(CultureInfo.InvariantCulture)
            );
            caseIndex++;

            var result = await orchestrator.ExecuteAsync(
                NewRequest(destination.Path, caseRoot, BackupOperation.Restore, password: casePassword),
                new RecordingProgress<BackupStatus>()
            );

            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    result.IsSuccess && result.Value.IsSuccess,
                    Is.False,
                    $"Restore succeeded even though the {name}."
                );
                Assert.That(
                    CollectCodes(result),
                    Does.Contain(expected),
                    $"Expected {expected} when the {name}."
                );
                Assert.That(
                    FilesUnder(caseRoot),
                    Is.Empty,
                    $"Restore left files behind even though the {name}."
                );
            }
        }
    }

    [Test]
    public async Task RestoreAsync_ManifestUnderDeclaresFileSize_StopsAtTheDeclaredByteBudget()
    {
        await using var provider = TestHost.CreateProvider();
        var orchestrator = provider.GetRequiredService<IBackupOrchestrator>();

        using var source = new TempDir();
        using var destination = new TempDir();
        using var restored = new TempDir();

        _ = source.WriteText("bomb.txt", new string('z', 5000));
        await CreateBackupAsync(orchestrator, source.Path, destination.Path, CompressionMode.Zstd);

        var (preamble, masterKey, manifest) = await OpenManifestAsync(
            provider,
            destination.Path,
            Password
        );
        var manifestKey = ExpandSubKey(masterKey, "manifest-encryption"u8);
        var entry = manifest.Files[0];

        Assert.That(
            entry.TotalSize,
            Is.EqualTo(5000L),
            "The fixture must store a large, highly compressible file, or the declared byte budget is never exercised."
        );

        var understated = manifest with { Files = [entry with { TotalSize = 1L }] };
        await SaveManifestAsync(provider, destination.Path, preamble, manifestKey, understated);

        var result = await orchestrator.ExecuteAsync(
            NewRequest(destination.Path, restored.Path, BackupOperation.Restore, CompressionMode.Zstd),
            new RecordingProgress<BackupStatus>()
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.IsSuccess && result.Value.IsSuccess,
                Is.False,
                "Restore accepted a chunk that decompresses past the size the manifest declares."
            );
            Assert.That(
                CollectCodes(result),
                Does.Contain(MessageCode.UnexpectedErrorFormat),
                "An over-expanding chunk must abort the run rather than be recorded as a per-file problem."
            );

            Assert.That(
                TotalBytesUnder(restored.Path),
                Is.LessThanOrEqualTo(1L),
                "Decompression wrote past the byte budget the manifest declared for the file."
            );
        }
    }

    [Test]
    public async Task RestoreAsync_ManifestRecordsTwoNoncesForOneChunkHash_IsRejectedInsteadOfGuessed()
    {
        await using var provider = TestHost.CreateProvider();
        var orchestrator = provider.GetRequiredService<IBackupOrchestrator>();

        using var source = new TempDir();
        using var destination = new TempDir();
        using var restored = new TempDir();

        const string SharedContent = "content whose chunk nonce is derived, not chosen";
        _ = source.WriteText("a.txt", SharedContent);
        _ = source.WriteText("b.txt", SharedContent);
        await CreateBackupAsync(orchestrator, source.Path, destination.Path, CompressionMode.None);

        var (preamble, masterKey, manifest) = await OpenManifestAsync(
            provider,
            destination.Path,
            Password
        );
        var manifestKey = ExpandSubKey(masterKey, "manifest-encryption"u8);

        Assert.That(
            manifest.Files.SelectMany(static f => f.Chunks).Select(static c => c.Hash).Distinct(),
            Has.Exactly(1).Items,
            "The fixture relies on both files deduplicating to one shared chunk."
        );

        var foreignNonce = Convert.ToBase64String(new byte[EncryptionConstants.NonceSize]);
        Assert.That(
            foreignNonce,
            Is.Not.EqualTo(manifest.Files[0].Chunks[0].Nonce),
            "The fixture must offer a nonce the engine did not derive."
        );

        var second = manifest.Files[1];
        var crafted = manifest with
        {
            Files =
            [
                manifest.Files[0],
                second with
                {
                    Chunks =
                    [
                        .. second.Chunks.Select(c => c with { Nonce = foreignNonce }),
                    ],
                },
            ],
        };
        await SaveManifestAsync(provider, destination.Path, preamble, manifestKey, crafted);

        var result = await orchestrator.ExecuteAsync(
            NewRequest(destination.Path, restored.Path, BackupOperation.Restore),
            new RecordingProgress<BackupStatus>()
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.IsSuccess && result.Value.IsSuccess,
                Is.False,
                "A chunk nonce is a deterministic function of the chunk hash, so a manifest offering two "
                    + "for one hash is crafted and must be rejected rather than resolved by trial decryption."
            );
            Assert.That(
                FilesUnder(restored.Path),
                Is.Empty,
                "The rejected manifest still produced output in the restore root."
            );
        }
    }

    /// <summary>
    /// Builds an AES plus PBKDF2 request that proceeds past advisory warnings, matching the cheapest
    /// key derivation the app offers so the suite stays affordable on CI.
    /// </summary>
    /// <param name="sourcePath">The tree to back up, or the backup directory to read from.</param>
    /// <param name="destinationPath">The directory the backup or the restored files are written to.</param>
    /// <param name="operation">The operation to dispatch.</param>
    /// <param name="compression">The compression mode applied to chunks before encryption.</param>
    /// <param name="password">The password to derive keys from; defaults to the fixture password.</param>
    /// <returns>The assembled request.</returns>
    private static BackupRequest NewRequest(
        string sourcePath,
        string destinationPath,
        BackupOperation operation,
        CompressionMode compression = CompressionMode.None,
        string password = Password
    )
    {
        return new BackupRequest(
            sourcePath,
            destinationPath,
            password,
            password,
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.PBKDF2,
            operation,
            compression,
            ProceedOnWarnings: true
        );
    }

    /// <summary>
    /// Creates a backup through the real orchestrator and fails the test if it did not succeed, so a
    /// broken fixture never masquerades as a security finding.
    /// </summary>
    /// <param name="orchestrator">The orchestrator that executes the create operation.</param>
    /// <param name="sourcePath">The directory to back up.</param>
    /// <param name="destinationPath">The directory the backup is written to.</param>
    /// <param name="compression">The compression mode applied to chunks before encryption.</param>
    /// <param name="password">The password to derive keys from; defaults to the fixture password.</param>
    /// <returns>A task that completes once the backup exists.</returns>
    private static async Task CreateBackupAsync(
        IBackupOrchestrator orchestrator,
        string sourcePath,
        string destinationPath,
        CompressionMode compression,
        string password = Password
    )
    {
        var result = await orchestrator.ExecuteAsync(
            NewRequest(sourcePath, destinationPath, BackupOperation.Create, compression, password),
            new RecordingProgress<BackupStatus>()
        );

        Assert.That(
            result.IsSuccess && result.Value.IsSuccess,
            Is.True,
            "The fixture could not create the backup under test."
        );
    }

    /// <summary>
    /// Opens a real backup's manifest the way the engine does: it reads the unencrypted preamble,
    /// re-derives the master key from the password and the recorded salt, and decrypts the document
    /// with the manifest sub-key.
    /// </summary>
    /// <remarks>
    /// Returning the master key lets a caller expand any of the four purpose-bound sub-keys without
    /// paying for a second key derivation, which dominates the cost of these tests.
    /// </remarks>
    /// <param name="provider">The provider holding the real manifest service and key derivation factory.</param>
    /// <param name="backupRoot">The backup root directory holding the manifest.</param>
    /// <param name="password">The password the backup was created with.</param>
    /// <returns>The parsed preamble, the derived master key, and the decrypted manifest.</returns>
    /// <exception cref="InvalidOperationException">The manifest is missing, malformed, or does not decrypt.</exception>
    private static async Task<(
        ManifestPreamble Preamble,
        byte[] MasterKey,
        ChunkManifestData Manifest
    )> OpenManifestAsync(IServiceProvider provider, string backupRoot, string password)
    {
        var manifestService = provider.GetRequiredService<IManifestService>();

        var preamble =
            await manifestService.ReadChunkManifestPreambleAsync(backupRoot, CancellationToken.None)
            ?? throw new InvalidOperationException("The backup under test has no readable manifest preamble.");

        var masterKey = provider
            .GetRequiredService<IKeyDerivationServiceFactory>()
            .Create(preamble.KeyDerivation)
            .DeriveKey(password, preamble.MasterSalt, EncryptionConstants.KeySize);

        var manifest =
            manifestService.DecryptChunkManifest(
                preamble,
                ExpandSubKey(masterKey, "manifest-encryption"u8)
            )
            ?? throw new InvalidOperationException("The manifest under test did not decrypt with the derived key.");

        return (preamble, masterKey, manifest);
    }

    /// <summary>
    /// Re-encrypts and writes a mutated manifest through the production manifest service, so the crafted
    /// document carries a valid authentication tag and the engine must reject it on its contents alone.
    /// </summary>
    /// <param name="provider">The provider holding the real manifest service.</param>
    /// <param name="backupRoot">The backup root directory the manifest is written into.</param>
    /// <param name="preamble">The preamble the backup was written with, supplying the algorithm.</param>
    /// <param name="manifestKey">The manifest encryption sub-key.</param>
    /// <param name="manifest">The mutated manifest to persist.</param>
    /// <returns>A task that completes once the manifest has been rewritten.</returns>
    private static async Task SaveManifestAsync(
        IServiceProvider provider,
        string backupRoot,
        ManifestPreamble preamble,
        byte[] manifestKey,
        ChunkManifestData manifest
    )
    {
        var errors = await provider
            .GetRequiredService<IManifestService>()
            .SaveChunkManifestAsync(
                manifest,
                backupRoot,
                manifestKey,
                preamble.Algorithm,
                CancellationToken.None
            );

        Assert.That(errors, Is.Empty, "The fixture could not rewrite the manifest under test.");
    }

    /// <summary>
    /// Expands one purpose-bound sub-key from the master key exactly as the backup engine does, with
    /// HKDF-Expand over SHA-256 and the label that names the sub-key's purpose.
    /// </summary>
    /// <remarks>
    /// A case that reasons about sub-key separation must first anchor at least one of these keys against
    /// a value the backup really produced — a chunk file name or a recorded nonce re-derived from the
    /// password alone — because a sub-key that had drifted from the engine's own derivation would let
    /// every separation assertion pass vacuously.
    /// </remarks>
    /// <param name="masterKey">The master key derived from the password and the backup's salt.</param>
    /// <param name="context">The label identifying the sub-key's purpose.</param>
    /// <returns>The derived sub-key.</returns>
    private static byte[] ExpandSubKey(byte[] masterKey, ReadOnlySpan<byte> context)
    {
        var subKey = new byte[EncryptionConstants.KeySize / 8];
        HKDF.Expand(HashAlgorithmName.SHA256, masterKey, subKey, context);
        return subKey;
    }

    /// <summary>
    /// Lists the names of the encrypted chunk files stored in a backup.
    /// </summary>
    /// <param name="backupRoot">The backup root directory holding the chunks directory.</param>
    /// <returns>The chunk file names, without their directory.</returns>
    private static string[] ChunkFileNames(string backupRoot)
    {
        var chunksDir = Path.Combine(backupRoot, BackupConstants.ChunksDirectoryName);
        var files = Directory.GetFiles(chunksDir, "*" + BackupConstants.AppFileExtension);
        var names = new string[files.Length];

        for (var i = 0; i < files.Length; i++)
        {
            names[i] = Path.GetFileName(files[i]);
        }

        return names;
    }

    /// <summary>
    /// Lists every file under a directory, treating a directory that was never created as empty.
    /// </summary>
    /// <param name="root">The directory to enumerate.</param>
    /// <returns>The absolute paths found beneath <paramref name="root"/>.</returns>
    private static string[] FilesUnder(string root)
    {
        return Directory.Exists(root)
            ? Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            : [];
    }

    /// <summary>
    /// Adds up the bytes actually written beneath a directory, treating a directory that was never
    /// created as zero bytes.
    /// </summary>
    /// <param name="root">The directory to measure.</param>
    /// <returns>The total size in bytes of every file beneath <paramref name="root"/>.</returns>
    private static long TotalBytesUnder(string root)
    {
        return FilesUnder(root).Sum(f => new FileInfo(f).Length);
    }

    /// <summary>
    /// Gathers the orchestrator's own error codes together with the per-file codes of the inner backup
    /// result, which is only read when the outer result succeeded because
    /// <see cref="BackupZCrypt.Application.ValueObjects.Result{T}.Value"/> throws on a failure.
    /// </summary>
    /// <param name="result">The orchestrator outcome to inspect.</param>
    /// <returns>The distinct message codes reported at either level.</returns>
    private static HashSet<MessageCode> CollectCodes(Result<BackupResult> result)
    {
        var codes = result.Errors.Select(static e => e.Code).ToHashSet();

        if (result.IsSuccess)
        {
            foreach (var error in result.Value.Errors)
            {
                _ = codes.Add(error.Code);
            }
        }

        return codes;
    }
}
