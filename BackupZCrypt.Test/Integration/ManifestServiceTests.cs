using System.Security.Cryptography;
using System.Text.Json;

using BackupZCrypt.Application.Services;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects.Manifest;
using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Factories.Interfaces;
using BackupZCrypt.Domain.Services.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Localization;
using BackupZCrypt.Test.Common;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

namespace BackupZCrypt.Test.Integration;

/// <summary>
/// Integration tests for the manifest service: backup detection, the encrypted on-disk round trip,
/// the anti-tamper checks that bind the unencrypted preamble to the encrypted document, and the
/// temp-file-plus-rename write that must never leave a half-written manifest in place of a valid one.
/// </summary>
/// <remarks>
/// <para>
/// Every test here supplies the manifest key directly instead of deriving one from a password, so the
/// whole fixture runs without a single key derivation and stays cheap on a slow CI runner.
/// </para>
/// <para>
/// Cancellation of a real file read is asserted with an <c>Is.InstanceOf</c> constraint rather than
/// <c>ThrowsAsync&lt;T&gt;</c>, because the I/O stack raises <see cref="TaskCanceledException"/>, which
/// derives from <see cref="OperationCanceledException"/>. Which of the two surfaces is an implementation
/// detail of the underlying read, so tightening the assertion back to the exact type makes the test fail
/// on an unrelated change.
/// </para>
/// </remarks>
public sealed class ManifestServiceTests
{
    /// <summary>
    /// The manifest key length in bytes, matching the production key size of
    /// <see cref="EncryptionConstants.KeySize"/> bits.
    /// </summary>
    private const int KeyLength = EncryptionConstants.KeySize / 8;

    /// <summary>
    /// The number of leading unencrypted bytes a chunked manifest reserves for its preamble header:
    /// the algorithm byte, the key derivation byte, and the master salt.
    /// </summary>
    private const int PreambleHeaderLength = 2 + EncryptionConstants.SaltSize;

    /// <summary>
    /// A well-formed Base64 master salt of exactly <see cref="EncryptionConstants.SaltSize"/> bytes, used
    /// by the rejection cases that need every field except the one under test to be valid.
    /// </summary>
    private static readonly string ValidMasterSalt = Convert.ToBase64String(new byte[EncryptionConstants.SaltSize]);

    /// <summary>
    /// Supplies manifests that must be rejected before anything is written, varying one field per case:
    /// a master salt of the wrong length, a master salt that is not Base64 at all, and each of the three
    /// algorithm identifiers set to a value outside its enum.
    /// </summary>
    /// <returns>One case per rejected combination of master salt and algorithm identifiers.</returns>
    private static IEnumerable<TestCaseData> RejectedManifests()
    {
        yield return new TestCaseData(
            Convert.ToBase64String(new byte[EncryptionConstants.SaltSize / 2]),
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.PBKDF2,
            CompressionMode.None
        );
        yield return new TestCaseData(
            "not base64 !!",
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.PBKDF2,
            CompressionMode.None
        );
        yield return new TestCaseData(
            ValidMasterSalt,
            (EncryptionAlgorithm)99,
            KeyDerivationAlgorithm.PBKDF2,
            CompressionMode.None
        );
        yield return new TestCaseData(
            ValidMasterSalt,
            EncryptionAlgorithm.Aes,
            (KeyDerivationAlgorithm)99,
            CompressionMode.None
        );
        yield return new TestCaseData(
            ValidMasterSalt,
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.PBKDF2,
            (CompressionMode)99
        );
    }

    /// <summary>
    /// Supplies the master salt encodings a manifest document must never be accepted with: absent,
    /// whitespace only, not Base64 at all, and Base64 that decodes to the wrong number of bytes.
    /// </summary>
    /// <returns>One malformed master salt per case.</returns>
    private static IEnumerable<string> MalformedDocumentMasterSalts()
    {
        yield return string.Empty;
        yield return "   ";
        yield return "not base64 !!";
        yield return Convert.ToBase64String(new byte[EncryptionConstants.SaltSize - 1]);
    }

    /// <summary>
    /// Builds a manifest with the requested number of entries, each carrying a distinct opaque hash so
    /// two manifests written into the same directory are trivially distinguishable once decrypted.
    /// </summary>
    /// <param name="fileCount">The number of file entries the manifest lists.</param>
    /// <returns>A manifest whose master salt is a fresh 32-byte value.</returns>
    private static ChunkManifestData NewManifest(int fileCount)
    {
        List<ChunkManifestFileEntry> files = [];

        for (var index = 0; index < fileCount; index++)
        {
            var raw = new byte[EncryptionConstants.SaltSize];
            Array.Fill(raw, (byte)index);
            var hash = Convert.ToBase64String(raw);

            files.Add(
                new ChunkManifestFileEntry(
                    $"file{index}.txt",
                    hash,
                    index,
                    [new ChunkManifestChunkRef(hash, index, Convert.ToBase64String(new byte[EncryptionConstants.NonceSize]))]
                )
            );
        }

        return new ChunkManifestData(
            new ManifestHeader(EncryptionAlgorithm.Aes, KeyDerivationAlgorithm.PBKDF2, CompressionMode.None),
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(EncryptionConstants.SaltSize)),
            files
        );
    }

    /// <summary>
    /// Hand-builds a manifest preamble whose encrypted document can be made to contradict the preamble
    /// it ships with. The associated data is always derived from the preamble's own values, so the AEAD
    /// tag verifies and decryption reaches the cross-checks that compare the document against the preamble.
    /// </summary>
    /// <param name="encryptionServiceFactory">The factory producing the AES strategy that encrypts the document.</param>
    /// <param name="key">The manifest encryption key.</param>
    /// <param name="preambleSalt">The 32-byte master salt recorded in the preamble and bound as associated data.</param>
    /// <param name="documentMasterSalt">The Base64 master salt echoed inside the encrypted document.</param>
    /// <param name="documentAlgorithm">The encryption algorithm the document declares.</param>
    /// <param name="documentKeyDerivation">The key derivation algorithm the document declares.</param>
    /// <returns>A preamble that always declares AES plus PBKDF2 and decrypts successfully.</returns>
    private static ManifestPreamble BuildCraftedPreamble(
        IEncryptionServiceFactory encryptionServiceFactory,
        byte[] key,
        byte[] preambleSalt,
        string documentMasterSalt,
        EncryptionAlgorithm documentAlgorithm = EncryptionAlgorithm.Aes,
        KeyDerivationAlgorithm documentKeyDerivation = KeyDerivationAlgorithm.PBKDF2
    )
    {
        ChunkManifestDocument document = new(
            documentAlgorithm,
            documentKeyDerivation,
            CompressionMode.None,
            documentMasterSalt,
            []
        );

        var associatedData = new byte[PreambleHeaderLength];
        associatedData[0] = (byte)EncryptionAlgorithm.Aes;
        associatedData[1] = (byte)KeyDerivationAlgorithm.PBKDF2;
        preambleSalt.CopyTo(associatedData, 2);

        var nonce = RandomNumberGenerator.GetBytes(EncryptionConstants.NonceSize);
        var ciphertext = encryptionServiceFactory
            .Create(EncryptionAlgorithm.Aes)
            .EncryptChunk(JsonSerializer.SerializeToUtf8Bytes(document), key, nonce, associatedData);

        return new ManifestPreamble(
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.PBKDF2,
            preambleSalt,
            nonce,
            ciphertext
        );
    }

    /// <summary>
    /// Builds a manifest service whose manifest reads always fail, so the error handling of
    /// <c>DetectManifestKindAsync</c> can be reached without depending on platform-specific
    /// file-system permissions, which differ between the Windows dev box and the Linux CI runner.
    /// </summary>
    /// <param name="readFailure">The exception every attempt to open the manifest throws.</param>
    /// <returns>A service that reports a manifest as present but cannot read it.</returns>
    private static ManifestService CreateServiceWithFailingManifestRead(Exception readFailure)
    {
        var fileOperations = Substitute.For<IFileOperationsService>();
        _ = fileOperations.DirectoryExists(Arg.Any<string>()).Returns(true);
        _ = fileOperations.FileExists(Arg.Any<string>()).Returns(true);
        _ = fileOperations
            .CombinePath(Arg.Any<string[]>())
            .Returns(call => Path.Combine(call.Arg<string[]>()));
        fileOperations
            .When(operations => operations.OpenReadStream(Arg.Any<string>(), Arg.Any<int>()))
            .Do(_ => throw readFailure);

        return new ManifestService(fileOperations, Substitute.For<IEncryptionServiceFactory>());
    }

    [Test]
    public async Task DetectManifestKindAsync_ManifestPresenceVaries_ReturnsExpectedKind()
    {
        await using var provider = TestHost.CreateProvider();
        var manifestService = provider.GetRequiredService<IManifestService>();

        using var empty = new TempDir();
        using var truncated = new TempDir();
        using var backup = new TempDir();

        _ = truncated.WriteFile(BackupConstants.ManifestFileName, []);
        _ = backup.WriteFile(BackupConstants.ManifestFileName, [0x01, 0x02, 0x03]);
        var sibling = backup.WriteText("readme.txt", "not the manifest");

        var emptyKind = await manifestService.DetectManifestKindAsync(empty.Path, CancellationToken.None);
        var truncatedKind = await manifestService.DetectManifestKindAsync(truncated.Path, CancellationToken.None);
        var backupKind = await manifestService.DetectManifestKindAsync(backup.Path, CancellationToken.None);
        var siblingKind = await manifestService.DetectManifestKindAsync(sibling, CancellationToken.None);
        var absentSiblingKind = await manifestService.DetectManifestKindAsync(
            Path.Combine(backup.Path, "never-created.txt"),
            CancellationToken.None
        );
        var rootlessKind = await manifestService.DetectManifestKindAsync("bare-name-with-no-directory.txt", CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                emptyKind,
                Is.EqualTo(ManifestKind.Missing),
                "a directory without a manifest was offered as a restorable backup"
            );
            Assert.That(
                truncatedKind,
                Is.EqualTo(ManifestKind.Missing),
                "a zero-byte manifest left by an interrupted write was treated as a restorable backup"
            );
            Assert.That(backupKind, Is.EqualTo(ManifestKind.Encrypted));
            Assert.That(
                siblingKind,
                Is.EqualTo(ManifestKind.Encrypted),
                "a path to a file inside the backup must resolve to the manifest in its parent directory"
            );
            Assert.That(
                absentSiblingKind,
                Is.EqualTo(ManifestKind.Encrypted),
                "only the parent directory of the supplied path decides the result, not whether that path exists"
            );
            Assert.That(
                rootlessKind,
                Is.EqualTo(ManifestKind.Missing),
                "a bare file name has no parent directory to search for a manifest"
            );
        }
    }

    [Test]
    public async Task DetectManifestKindAsync_ManifestCannotBeRead_ReportsMissingInsteadOfThrowing()
    {
        var manifestService = CreateServiceWithFailingManifestRead(new UnauthorizedAccessException("injected read failure"));

        var kind = await manifestService.DetectManifestKindAsync(
            Path.Combine(Path.GetTempPath(), "bzc-unreadable"),
            CancellationToken.None
        );

        Assert.That(
            kind,
            Is.EqualTo(ManifestKind.Missing),
            "an unreadable manifest must degrade to Missing rather than crash the folder-picker flow"
        );
    }

    [Test]
    public void DetectManifestKindAsync_ManifestReadCancelled_RethrowsCancellation()
    {
        var manifestService = CreateServiceWithFailingManifestRead(new OperationCanceledException());

        _ = Assert.ThrowsAsync<OperationCanceledException>(
            () => manifestService.DetectManifestKindAsync(
                Path.Combine(Path.GetTempPath(), "bzc-cancelled"),
                CancellationToken.None
            )
        );
    }

    [Test]
    public async Task SaveChunkManifestAsync_RoundTripsThroughDisk_PreservesEveryFieldAndOrdersEntriesOrdinally()
    {
        await using var provider = TestHost.CreateProvider();
        var manifestService = provider.GetRequiredService<IManifestService>();

        using var backup = new TempDir();
        var key = RandomNumberGenerator.GetBytes(KeyLength);
        var masterSalt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(EncryptionConstants.SaltSize));

        ChunkManifestData original = new(
            new ManifestHeader(EncryptionAlgorithm.Aes, KeyDerivationAlgorithm.PBKDF2, CompressionMode.Zstd),
            masterSalt,
            [
                new ChunkManifestFileEntry("z.txt", "emptyfilehash", 0, []),
                new ChunkManifestFileEntry(
                    "docs/notes.md",
                    "notesfilehash",
                    9001L,
                    [
                        new ChunkManifestChunkRef("chunkhashone", 4096, "nonceone"),
                        new ChunkManifestChunkRef("chunkhashtwo", 4905, "noncetwo"),
                    ]
                ),
                new ChunkManifestFileEntry("a.txt", "afilehash", 12, [new ChunkManifestChunkRef("chunkhashthree", 12, "noncethree")]),
            ]
        );

        var errors = await manifestService.SaveChunkManifestAsync(
            original,
            backup.Path,
            key,
            EncryptionAlgorithm.Aes,
            CancellationToken.None
        );

        var preamble = await manifestService.ReadChunkManifestPreambleAsync(backup.Path, CancellationToken.None);
        Assert.That(errors, Is.Empty, "saving a well-formed manifest reported errors");
        Assert.That(preamble, Is.Not.Null, "a manifest that was just written could not be parsed back");

        var decrypted = manifestService.DecryptChunkManifest(preamble!, key);
        Assert.That(decrypted, Is.Not.Null, "a manifest that was just written could not be decrypted with the key it was written under");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(preamble!.Algorithm, Is.EqualTo(EncryptionAlgorithm.Aes));
            Assert.That(preamble.KeyDerivation, Is.EqualTo(KeyDerivationAlgorithm.PBKDF2));
            Assert.That(Convert.ToBase64String(preamble.MasterSalt), Is.EqualTo(masterSalt));
            Assert.That(preamble.Nonce, Has.Length.EqualTo(EncryptionConstants.NonceSize));
            Assert.That(decrypted!.Header, Is.EqualTo(original.Header), "the header algorithms did not survive the round trip");
            Assert.That(decrypted.MasterSalt, Is.EqualTo(masterSalt));
            Assert.That(
                decrypted.Files.Select(static file => file.OriginalPath),
                Is.EqualTo(new[] { "a.txt", "docs/notes.md", "z.txt" }),
                "entries must be stored in ordinal path order so the same content always yields the same manifest"
            );

            foreach (var expected in original.Files)
            {
                var actual = decrypted.Files.Single(file => StringComparer.Ordinal.Equals(file.OriginalPath, expected.OriginalPath));
                Assert.That(actual.FileHash, Is.EqualTo(expected.FileHash), $"{expected.OriginalPath}: file hash");
                Assert.That(actual.TotalSize, Is.EqualTo(expected.TotalSize), $"{expected.OriginalPath}: total size");
                Assert.That(actual.Chunks, Is.EqualTo(expected.Chunks), $"{expected.OriginalPath}: chunk references");
            }
        }
    }

    [Test]
    public async Task SaveChunkManifestAsync_OverExistingManifest_ReplacesItAndLeavesNoTempFileBehind()
    {
        await using var provider = TestHost.CreateProvider();
        var manifestService = provider.GetRequiredService<IManifestService>();

        using var backup = new TempDir();
        var firstKey = RandomNumberGenerator.GetBytes(KeyLength);
        var secondKey = RandomNumberGenerator.GetBytes(KeyLength);

        _ = await manifestService.SaveChunkManifestAsync(
            NewManifest(1),
            backup.Path,
            firstKey,
            EncryptionAlgorithm.Aes,
            CancellationToken.None
        );

        var errors = await manifestService.SaveChunkManifestAsync(
            NewManifest(3),
            backup.Path,
            secondKey,
            EncryptionAlgorithm.Aes,
            CancellationToken.None
        );

        var preamble = await manifestService.ReadChunkManifestPreambleAsync(backup.Path, CancellationToken.None);
        Assert.That(preamble, Is.Not.Null, "the replaced manifest could no longer be parsed");

        var reread = manifestService.DecryptChunkManifest(preamble!, secondKey);
        Assert.That(
            reread,
            Is.Not.Null,
            "the replacement manifest did not read back cleanly, so the write appended to the old one instead of replacing it"
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(errors, Is.Empty);
            Assert.That(Directory.GetFiles(backup.Path), Has.Length.EqualTo(1), "the atomic write left an extra file in the backup root");
            Assert.That(
                File.Exists(Path.Combine(backup.Path, BackupConstants.ManifestFileName + ".tmp")),
                Is.False,
                "the atomic write left its temp manifest behind for the next run to trip over"
            );
            Assert.That(reread!.Files, Has.Count.EqualTo(3), "the manifest read back is not the one that was written last");
        }
    }

    [Test]
    public async Task DecryptChunkManifest_KeyOrPreambleTampered_ReturnsNull()
    {
        await using var provider = TestHost.CreateProvider();
        var manifestService = provider.GetRequiredService<IManifestService>();

        using var backup = new TempDir();
        var key = RandomNumberGenerator.GetBytes(KeyLength);

        _ = await manifestService.SaveChunkManifestAsync(
            NewManifest(2),
            backup.Path,
            key,
            EncryptionAlgorithm.Aes,
            CancellationToken.None
        );

        var stored = await manifestService.ReadChunkManifestPreambleAsync(backup.Path, CancellationToken.None);
        Assert.That(stored, Is.Not.Null, "a manifest that was just written could not be parsed back");

        var preamble = stored!;
        var wrongKey = RandomNumberGenerator.GetBytes(KeyLength);
        var alteredSalt = preamble.MasterSalt.ToArray();
        alteredSalt[0] ^= 0xFF;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                manifestService.DecryptChunkManifest(preamble, key),
                Is.Not.Null,
                "the untampered control case must decrypt, otherwise the rejections below prove nothing"
            );
            Assert.That(
                manifestService.DecryptChunkManifest(preamble, wrongKey),
                Is.Null,
                "a manifest opened with the wrong password must not decrypt"
            );
            Assert.That(
                manifestService.DecryptChunkManifest(preamble with { Algorithm = EncryptionAlgorithm.Twofish }, key),
                Is.Null,
                "the unencrypted algorithm byte must be bound into the authentication tag, so downgrading it cannot open the manifest"
            );
            Assert.That(
                manifestService.DecryptChunkManifest(preamble with { KeyDerivation = KeyDerivationAlgorithm.Scrypt }, key),
                Is.Null,
                "the unencrypted key derivation byte must be bound into the authentication tag"
            );
            Assert.That(
                manifestService.DecryptChunkManifest(preamble with { MasterSalt = alteredSalt }, key),
                Is.Null,
                "a single flipped bit in the unencrypted master salt must invalidate the whole manifest"
            );
        }
    }

    [Test]
    public async Task DecryptChunkManifest_DocumentContradictsPreamble_ReturnsNull()
    {
        await using var provider = TestHost.CreateProvider();
        var manifestService = provider.GetRequiredService<IManifestService>();
        var encryptionServiceFactory = provider.GetRequiredService<IEncryptionServiceFactory>();

        var key = RandomNumberGenerator.GetBytes(KeyLength);
        var preambleSalt = RandomNumberGenerator.GetBytes(EncryptionConstants.SaltSize);
        var foreignSalt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(EncryptionConstants.SaltSize));
        var matchingSalt = Convert.ToBase64String(preambleSalt);

        var control = BuildCraftedPreamble(encryptionServiceFactory, key, preambleSalt, matchingSalt);
        var saltEcho = BuildCraftedPreamble(encryptionServiceFactory, key, preambleSalt, foreignSalt);
        var algorithmEcho = BuildCraftedPreamble(
            encryptionServiceFactory,
            key,
            preambleSalt,
            matchingSalt,
            documentAlgorithm: EncryptionAlgorithm.Twofish
        );
        var keyDerivationEcho = BuildCraftedPreamble(
            encryptionServiceFactory,
            key,
            preambleSalt,
            matchingSalt,
            documentKeyDerivation: KeyDerivationAlgorithm.Scrypt
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                manifestService.DecryptChunkManifest(control, key),
                Is.Not.Null,
                "the control manifest must decrypt, otherwise the rejections below prove nothing"
            );
            Assert.That(
                manifestService.DecryptChunkManifest(saltEcho, key),
                Is.Null,
                "a manifest whose embedded master salt disagrees with its preamble was accepted"
            );
            Assert.That(
                manifestService.DecryptChunkManifest(algorithmEcho, key),
                Is.Null,
                "a document declaring a different cipher than the preamble was accepted"
            );
            Assert.That(
                manifestService.DecryptChunkManifest(keyDerivationEcho, key),
                Is.Null,
                "a document declaring a different key derivation than the preamble was accepted"
            );
        }
    }

    [TestCase(0, 0x00, 0x00)]
    [TestCase(1, 0x00, 0x00)]
    [TestCase(20, 0x00, 0x00)]
    [TestCase(100, 0x7F, 0x00)]
    [TestCase(100, 0x00, 0x7F)]
    public async Task ReadChunkManifestPreambleAsync_MalformedManifest_ReturnsNull(int length, int algorithmByte, int keyDerivationByte)
    {
        await using var provider = TestHost.CreateProvider();
        var manifestService = provider.GetRequiredService<IManifestService>();

        using var backup = new TempDir();

        var raw = new byte[length];
        if (length > 1)
        {
            raw[0] = (byte)algorithmByte;
            raw[1] = (byte)keyDerivationByte;
        }

        _ = backup.WriteFile(BackupConstants.ManifestFileName, raw);

        var preamble = await manifestService.ReadChunkManifestPreambleAsync(backup.Path, CancellationToken.None);

        Assert.That(
            preamble,
            Is.Null,
            "a truncated manifest or an out-of-range algorithm identifier must fail closed instead of "
                + "producing a preamble holding garbage salt and nonce slices"
        );
    }

    [Test]
    public async Task ReadChunkManifestPreambleAsync_MissingOrHeaderOnlyManifest_NeverYieldsAReadableManifest()
    {
        await using var provider = TestHost.CreateProvider();
        var manifestService = provider.GetRequiredService<IManifestService>();

        using var missing = new TempDir();
        using var headerOnly = new TempDir();

        _ = headerOnly.WriteFile(
            BackupConstants.ManifestFileName,
            new byte[PreambleHeaderLength + EncryptionConstants.NonceSize]
        );

        var missingPreamble = await manifestService.ReadChunkManifestPreambleAsync(missing.Path, CancellationToken.None);
        var headerOnlyPreamble = await manifestService.ReadChunkManifestPreambleAsync(headerOnly.Path, CancellationToken.None);
        Assert.That(headerOnlyPreamble, Is.Not.Null, "a manifest carrying a complete header and nonce must still parse");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(missingPreamble, Is.Null, "a directory with no manifest must not produce a preamble");
            Assert.That(headerOnlyPreamble!.EncryptedPayload, Is.Empty);
            Assert.That(
                manifestService.DecryptChunkManifest(headerOnlyPreamble, new byte[KeyLength]),
                Is.Null,
                "a manifest truncated to its header and nonce must never open, whatever key is supplied"
            );
        }
    }

    [TestCaseSource(nameof(RejectedManifests))]
    public async Task SaveChunkManifestAsync_ManifestParametersInvalid_ReportsWriteFailureAndWritesNothing(
        string masterSalt,
        EncryptionAlgorithm algorithm,
        KeyDerivationAlgorithm keyDerivation,
        CompressionMode compression
    )
    {
        await using var provider = TestHost.CreateProvider();
        var manifestService = provider.GetRequiredService<IManifestService>();

        using var backup = new TempDir();

        ChunkManifestData manifest = new(
            new ManifestHeader(EncryptionAlgorithm.Aes, keyDerivation, compression),
            masterSalt,
            []
        );

        var errors = await manifestService.SaveChunkManifestAsync(
            manifest,
            backup.Path,
            new byte[KeyLength],
            algorithm,
            CancellationToken.None
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(errors, Has.Count.EqualTo(1));
            Assert.That(
                errors[0].Code,
                Is.AnyOf(
                    MessageCode.ManifestInvalidMasterSalt,
                    MessageCode.ManifestUnsupportedAlgorithm
                ),
                "the rejection must name which parameter was wrong, and must carry no untranslatable text"
            );
            Assert.That(
                errors[0].Args,
                Is.Empty,
                "these codes describe the whole problem; splicing an English sentence in as a format "
                    + "argument is what put untranslated text in front of a Spanish user before"
            );
            Assert.That(
                Directory.GetFiles(backup.Path),
                Is.Empty,
                "a manifest that would be unreadable forever must not reach the destination directory at all"
            );
        }
    }

    [Test]
    public async Task SaveChunkManifestAsync_RenameFails_KeepsTheExistingManifestAndDeletesTheTempFile()
    {
        await using var provider = TestHost.CreateProvider();
        var realFileOperations = provider.GetRequiredService<IFileOperationsService>();
        var encryptionServiceFactory = provider.GetRequiredService<IEncryptionServiceFactory>();
        var manifestService = new ManifestService(realFileOperations, encryptionServiceFactory);

        using var backup = new TempDir();
        var key = RandomNumberGenerator.GetBytes(KeyLength);

        _ = await manifestService.SaveChunkManifestAsync(
            NewManifest(2),
            backup.Path,
            key,
            EncryptionAlgorithm.Aes,
            CancellationToken.None
        );

        var manifestPath = Path.Combine(backup.Path, BackupConstants.ManifestFileName);
        var originalBytes = await File.ReadAllBytesAsync(manifestPath, CancellationToken.None);

        var failingFileOperations = Substitute.For<IFileOperationsService>();
        _ = failingFileOperations
            .WriteAllBytesAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(call => realFileOperations.WriteAllBytesAsync(
                call.ArgAt<string>(0),
                call.ArgAt<byte[]>(1),
                call.ArgAt<CancellationToken>(2)
            ));
        failingFileOperations
            .When(operations => operations.MoveFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>()))
            .Do(_ => throw new IOException("injected rename failure"));
        failingFileOperations
            .When(operations => operations.DeleteFile(Arg.Any<string>()))
            .Do(call => realFileOperations.DeleteFile(call.ArgAt<string>(0)));

        var failingService = new ManifestService(failingFileOperations, encryptionServiceFactory);

        var errors = await failingService.SaveChunkManifestAsync(
            NewManifest(5),
            backup.Path,
            key,
            EncryptionAlgorithm.Aes,
            CancellationToken.None
        );

        var survivingBytes = await File.ReadAllBytesAsync(manifestPath, CancellationToken.None);
        var preamble = await manifestService.ReadChunkManifestPreambleAsync(backup.Path, CancellationToken.None);
        Assert.That(preamble, Is.Not.Null, "a failed save destroyed the manifest that was already in place");

        var survivor = manifestService.DecryptChunkManifest(preamble!, key);
        Assert.That(survivor, Is.Not.Null, "the manifest already in place no longer decrypts after an unrelated save failed");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(errors, Has.Count.EqualTo(1));
            Assert.That(errors[0].Code, Is.EqualTo(MessageCode.ManifestWriteFailedFormat));
            Assert.That(
                errors[0].Args,
                Has.Some.EqualTo("injected rename failure"),
                "the original write failure must be reported, not whatever happened while cleaning up after it"
            );
            Assert.That(
                File.Exists(manifestPath + ".tmp"),
                Is.False,
                "a failed save left a temp manifest behind for the next run to trip over"
            );
            Assert.That(survivingBytes, Is.EqualTo(originalBytes), "a failed save damaged the manifest that was already in place");
            Assert.That(
                survivor!.Files,
                Has.Count.EqualTo(2),
                "the failed save partially replaced the contents of the manifest already in place"
            );
        }
    }

    [Test]
    public async Task SaveChunkManifestAsync_RenameAndTempCleanupBothFail_ReportsTheRenameFailureNotTheCleanupFailure()
    {
        await using var provider = TestHost.CreateProvider();
        var realFileOperations = provider.GetRequiredService<IFileOperationsService>();
        var encryptionServiceFactory = provider.GetRequiredService<IEncryptionServiceFactory>();

        using var backup = new TempDir();

        var failingFileOperations = Substitute.For<IFileOperationsService>();
        _ = failingFileOperations
            .WriteAllBytesAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(call => realFileOperations.WriteAllBytesAsync(
                call.ArgAt<string>(0),
                call.ArgAt<byte[]>(1),
                call.ArgAt<CancellationToken>(2)
            ));
        failingFileOperations
            .When(operations => operations.MoveFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>()))
            .Do(_ => throw new IOException("injected rename failure"));
        failingFileOperations
            .When(operations => operations.DeleteFile(Arg.Any<string>()))
            .Do(_ => throw new UnauthorizedAccessException("injected cleanup failure"));

        var failingService = new ManifestService(failingFileOperations, encryptionServiceFactory);

        var errors = await failingService.SaveChunkManifestAsync(
            NewManifest(2),
            backup.Path,
            RandomNumberGenerator.GetBytes(KeyLength),
            EncryptionAlgorithm.Aes,
            CancellationToken.None
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(errors, Has.Count.EqualTo(1));
            Assert.That(errors[0].Code, Is.EqualTo(MessageCode.ManifestWriteFailedFormat));
            Assert.That(
                errors[0].Args,
                Has.Some.EqualTo("injected rename failure"),
                "a temp file that cannot be cleaned up must not mask the write failure that caused the cleanup"
            );
            Assert.That(
                errors[0].Args,
                Has.None.EqualTo("injected cleanup failure"),
                "the reported cause was the failed cleanup rather than the failure that actually broke the save"
            );
            Assert.That(
                File.Exists(Path.Combine(backup.Path, BackupConstants.ManifestFileName)),
                Is.False,
                "a save whose rename never happened must not leave a manifest at the destination"
            );
        }
    }

    [TestCaseSource(nameof(MalformedDocumentMasterSalts))]
    public async Task DecryptChunkManifest_DocumentMasterSaltIsNotDecodable_ReturnsNull(string documentMasterSalt)
    {
        await using var provider = TestHost.CreateProvider();
        var manifestService = provider.GetRequiredService<IManifestService>();
        var encryptionServiceFactory = provider.GetRequiredService<IEncryptionServiceFactory>();

        var key = RandomNumberGenerator.GetBytes(KeyLength);
        var preambleSalt = RandomNumberGenerator.GetBytes(EncryptionConstants.SaltSize);

        var crafted = BuildCraftedPreamble(encryptionServiceFactory, key, preambleSalt, documentMasterSalt);

        Assert.That(
            manifestService.DecryptChunkManifest(crafted, key),
            Is.Null,
            "a document whose embedded master salt cannot be decoded to 32 bytes has nothing to compare against the "
                + "preamble, so it must be rejected even though its authentication tag verifies"
        );
    }

    [Test]
    public async Task ReadChunkManifestPreambleAsync_CancelledDuringRead_RethrowsInsteadOfReportingNoManifest()
    {
        await using var provider = TestHost.CreateProvider();
        var manifestService = provider.GetRequiredService<IManifestService>();

        using var backup = new TempDir();
        var key = RandomNumberGenerator.GetBytes(KeyLength);

        _ = await manifestService.SaveChunkManifestAsync(
            NewManifest(1),
            backup.Path,
            key,
            EncryptionAlgorithm.Aes,
            CancellationToken.None
        );

        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        _ = Assert.ThrowsAsync(
            Is.InstanceOf<OperationCanceledException>(),
            () => manifestService.ReadChunkManifestPreambleAsync(backup.Path, cancellation.Token)
        );

        var afterwards = await manifestService.ReadChunkManifestPreambleAsync(backup.Path, CancellationToken.None);

        Assert.That(
            afterwards,
            Is.Not.Null,
            "the manifest itself is intact, so a cancelled read must surface as cancellation rather than as the "
                + "backup having no manifest at all"
        );
    }

    [Test]
    public async Task DetectManifestKindAsync_ManifestReadFailsMidStream_ReportsMissingAndStillClosesTheStream()
    {
        var stream = new FailingReadStream();
        var fileOperations = Substitute.For<IFileOperationsService>();
        _ = fileOperations.DirectoryExists(Arg.Any<string>()).Returns(true);
        _ = fileOperations.FileExists(Arg.Any<string>()).Returns(true);
        _ = fileOperations
            .CombinePath(Arg.Any<string[]>())
            .Returns(call => Path.Combine(call.Arg<string[]>()));
        _ = fileOperations.OpenReadStream(Arg.Any<string>(), Arg.Any<int>()).Returns(stream);

        var manifestService = new ManifestService(fileOperations, Substitute.For<IEncryptionServiceFactory>());

        var kind = await manifestService.DetectManifestKindAsync(
            Path.Combine(Path.GetTempPath(), "bzc-midread"),
            CancellationToken.None
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                kind,
                Is.EqualTo(ManifestKind.Missing),
                "a manifest that opens but cannot be read must degrade to Missing rather than crash the folder-picker flow"
            );
            Assert.That(
                stream.WasDisposed,
                Is.True,
                "a manifest read that fails part way through must still release the handle it opened"
            );
        }
    }

    /// <summary>
    /// A manifest stream that opens successfully and then fails on the first read, so the detection path
    /// has to dispose a stream it already owns while the failure unwinds.
    /// </summary>
    /// <remarks>
    /// Disposal yields before it records itself, so the awaited dispose genuinely suspends while the read
    /// failure unwinds instead of completing inline.
    /// </remarks>
    private sealed class FailingReadStream : MemoryStream
    {
        /// <summary>
        /// Gets a value indicating whether the stream was asynchronously disposed.
        /// </summary>
        public bool WasDisposed { get; private set; }

        /// <inheritdoc/>
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            throw new IOException("injected mid-read failure");
        }

        /// <inheritdoc/>
        public override async ValueTask DisposeAsync()
        {
            await Task.Yield();

            this.WasDisposed = true;

            await base.DisposeAsync();
        }
    }
}
