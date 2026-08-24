using System.Globalization;
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
/// Cancellation of a real file read is asserted with <c>Assert.ThrowsAnyAsync&lt;T&gt;</c> rather than
/// <c>Assert.ThrowsAsync&lt;T&gt;</c>, because the I/O stack raises <see cref="TaskCanceledException"/>, which
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
    /// The only two codes a rejected manifest may report, held in a static field because a constant array
    /// built inline at the assertion site is what <c>CA1861</c> exists to stop.
    /// </summary>
    private static readonly MessageCode[] RejectionCodes =
    [
        MessageCode.ManifestInvalidMasterSalt,
        MessageCode.ManifestUnsupportedAlgorithm,
    ];

    /// <summary>
    /// Supplies manifests that must be rejected before anything is written, varying one field per case:
    /// a master salt of the wrong length, a master salt that is not Base64 at all, and each of the three
    /// algorithm identifiers set to a value outside its enum.
    /// </summary>
    /// <returns>One case per rejected combination of master salt and algorithm identifiers.</returns>
    public static TheoryData<string, EncryptionAlgorithm, KeyDerivationAlgorithm, CompressionMode> RejectedManifests()
    {
        return new()
        {
            {
                Convert.ToBase64String(new byte[EncryptionConstants.SaltSize / 2]),
                EncryptionAlgorithm.Aes,
                KeyDerivationAlgorithm.PBKDF2,
                CompressionMode.None
            },
            {
                "not base64 !!",
                EncryptionAlgorithm.Aes,
                KeyDerivationAlgorithm.PBKDF2,
                CompressionMode.None
            },
            {
                ValidMasterSalt,
                (EncryptionAlgorithm)99,
                KeyDerivationAlgorithm.PBKDF2,
                CompressionMode.None
            },
            {
                ValidMasterSalt,
                EncryptionAlgorithm.Aes,
                (KeyDerivationAlgorithm)99,
                CompressionMode.None
            },
            {
                ValidMasterSalt,
                EncryptionAlgorithm.Aes,
                KeyDerivationAlgorithm.PBKDF2,
                (CompressionMode)99
            },
        };
    }

    /// <summary>
    /// Supplies the master salt encodings a manifest document must never be accepted with: absent,
    /// whitespace only, not Base64 at all, and Base64 that decodes to the wrong number of bytes.
    /// </summary>
    /// <returns>One malformed master salt per case.</returns>
    public static TheoryData<string> MalformedDocumentMasterSalts()
    {
        return new()
        {
            string.Empty,
            "   ",
            "not base64 !!",
            Convert.ToBase64String(new byte[EncryptionConstants.SaltSize - 1]),
        };
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
                    string.Create(CultureInfo.InvariantCulture, $"file{index}.txt"),
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
            .Returns(call => Path.Combine(call.Arg<string[]>() ?? []));
        fileOperations
            .When(operations => operations.OpenReadStream(Arg.Any<string>(), Arg.Any<int>()))
            .Do(_ => throw readFailure);

        return new ManifestService(fileOperations, Substitute.For<IEncryptionServiceFactory>());
    }

    [Fact]
    internal async Task DetectManifestKindAsync_ManifestPresenceVaries_ReturnsExpectedKind()
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

        Assert.Multiple(
            () => Assert.Equal(ManifestKind.Missing, emptyKind),
            () => Assert.Equal(ManifestKind.Missing, truncatedKind),
            () => Assert.Equal(ManifestKind.Encrypted, backupKind),
            () => Assert.Equal(ManifestKind.Encrypted, siblingKind),
            () => Assert.Equal(ManifestKind.Encrypted, absentSiblingKind),
            () => Assert.Equal(ManifestKind.Missing, rootlessKind)
        );
    }

    [Fact]
    internal async Task DetectManifestKindAsync_ManifestCannotBeRead_ReportsMissingInsteadOfThrowing()
    {
        var manifestService = CreateServiceWithFailingManifestRead(new UnauthorizedAccessException("injected read failure"));

        var kind = await manifestService.DetectManifestKindAsync(
            Path.Combine(Path.GetTempPath(), "bzc-unreadable"),
            CancellationToken.None
        );

        Assert.Equal(ManifestKind.Missing, kind);
    }

    [Fact]
    internal async Task DetectManifestKindAsync_ManifestReadCancelled_RethrowsCancellation()
    {
        var manifestService = CreateServiceWithFailingManifestRead(new OperationCanceledException());

        _ = await Assert.ThrowsAsync<OperationCanceledException>(
            () => manifestService.DetectManifestKindAsync(
                Path.Combine(Path.GetTempPath(), "bzc-cancelled"),
                CancellationToken.None
            )
        );
    }

    [Fact]
    internal async Task SaveChunkManifestAsync_RoundTripsThroughDisk_PreservesEveryFieldAndOrdersEntriesOrdinally()
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
        Assert.Multiple(
            () => Assert.Empty(errors),
            () => Assert.NotNull(preamble)
        );

        var decrypted = manifestService.DecryptChunkManifest(preamble!, key);
        Assert.NotNull(decrypted);

        // The expected order is a typed local rather than an inline collection expression so both sides of
        // the comparison below are string[]: Assert.Equal then compares them element by element, in order.
        string[] expectedPathOrder = ["a.txt", "docs/notes.md", "z.txt"];

        Assert.Multiple(
            () => Assert.Equal(EncryptionAlgorithm.Aes, preamble!.Algorithm),
            () => Assert.Equal(KeyDerivationAlgorithm.PBKDF2, preamble!.KeyDerivation),
            () => Assert.Equal(masterSalt, Convert.ToBase64String(preamble!.MasterSalt)),
            () => Assert.Equal(EncryptionConstants.NonceSize, preamble!.Nonce.Length),
            () => Assert.Equal(original.Header, decrypted!.Header),
            () => Assert.Equal(masterSalt, decrypted!.MasterSalt),
            () => Assert.Equal(expectedPathOrder, decrypted!.Files.Select(static file => file.OriginalPath).ToArray()),
            () =>
            {
                foreach (var expected in original.Files)
                {
                    var actual = decrypted!.Files.Single(file => StringComparer.Ordinal.Equals(file.OriginalPath, expected.OriginalPath));
                    Assert.Equal(expected.FileHash, actual.FileHash);
                    Assert.Equal(expected.TotalSize, actual.TotalSize);
                    Assert.Equal(expected.Chunks, actual.Chunks);
                }
            }
        );
    }

    [Fact]
    internal async Task SaveChunkManifestAsync_OverExistingManifest_ReplacesItAndLeavesNoTempFileBehind()
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
        Assert.NotNull(preamble);

        var reread = manifestService.DecryptChunkManifest(preamble!, secondKey);
        Assert.NotNull(reread);

        Assert.Multiple(
            () => Assert.Empty(errors),
            () => Assert.Single(Directory.GetFiles(backup.Path)),
            () => Assert.False(
                File.Exists(Path.Combine(backup.Path, BackupConstants.ManifestFileName + ".tmp")),
                "the atomic write left its temp manifest behind for the next run to trip over"
            ),
            () => Assert.Equal(3, reread!.Files.Count)
        );
    }

    [Fact]
    internal async Task DecryptChunkManifest_KeyOrPreambleTampered_ReturnsNull()
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
        Assert.NotNull(stored);

        var preamble = stored!;
        var wrongKey = RandomNumberGenerator.GetBytes(KeyLength);
        var alteredSalt = preamble.MasterSalt.ToArray();
        alteredSalt[0] ^= 0xFF;

        Assert.Multiple(
            () => Assert.NotNull(manifestService.DecryptChunkManifest(preamble, key)),
            () => Assert.Null(manifestService.DecryptChunkManifest(preamble, wrongKey)),
            () => Assert.Null(manifestService.DecryptChunkManifest(preamble with { Algorithm = EncryptionAlgorithm.Twofish }, key)),
            () => Assert.Null(manifestService.DecryptChunkManifest(preamble with { KeyDerivation = KeyDerivationAlgorithm.Scrypt }, key)),
            () => Assert.Null(manifestService.DecryptChunkManifest(preamble with { MasterSalt = alteredSalt }, key))
        );
    }

    [Fact]
    internal async Task DecryptChunkManifest_DocumentContradictsPreamble_ReturnsNull()
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

        Assert.Multiple(
            () => Assert.NotNull(manifestService.DecryptChunkManifest(control, key)),
            () => Assert.Null(manifestService.DecryptChunkManifest(saltEcho, key)),
            () => Assert.Null(manifestService.DecryptChunkManifest(algorithmEcho, key)),
            () => Assert.Null(manifestService.DecryptChunkManifest(keyDerivationEcho, key))
        );
    }

    [Theory]
    [InlineData(0, 0x00, 0x00)]
    [InlineData(1, 0x00, 0x00)]
    [InlineData(20, 0x00, 0x00)]
    [InlineData(100, 0x7F, 0x00)]
    [InlineData(100, 0x00, 0x7F)]
    internal async Task ReadChunkManifestPreambleAsync_MalformedManifest_ReturnsNull(int length, int algorithmByte, int keyDerivationByte)
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

        Assert.Null(preamble);
    }

    [Fact]
    internal async Task ReadChunkManifestPreambleAsync_MissingOrHeaderOnlyManifest_NeverYieldsAReadableManifest()
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
        Assert.NotNull(headerOnlyPreamble);

        Assert.Multiple(
            () => Assert.Null(missingPreamble),
            () => Assert.Empty(headerOnlyPreamble!.EncryptedPayload),
            () => Assert.Null(manifestService.DecryptChunkManifest(headerOnlyPreamble!, new byte[KeyLength]))
        );
    }

    [Theory]
    [MemberData(nameof(RejectedManifests))]
    internal async Task SaveChunkManifestAsync_ManifestParametersInvalid_ReportsWriteFailureAndWritesNothing(
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

        Assert.Multiple(
            () => Assert.Single(errors),
            () => Assert.Contains(errors[0].Code, RejectionCodes),
            () => Assert.Empty(errors[0].Args),
            () => Assert.Empty(Directory.GetFiles(backup.Path))
        );
    }

    [Fact]
    internal async Task SaveChunkManifestAsync_AtomicWriteFails_KeepsExistingManifestAndLeavesNoTempFile()
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
            .WriteFileAtomicallyAsync(
                Arg.Any<string>(),
                Arg.Any<Func<Stream, CancellationToken, Task>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromException(new IOException("injected atomic write failure")));

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
        Assert.NotNull(preamble);

        var survivor = manifestService.DecryptChunkManifest(preamble!, key);
        Assert.NotNull(survivor);

        Assert.Multiple(
            () => Assert.Single(errors),
            () => Assert.Equal(MessageCode.ManifestWriteFailedFormat, errors[0].Code),
            () => Assert.Contains<object>("injected atomic write failure", errors[0].Args),
            () =>
                Assert.Empty(
                    Directory.GetFiles(backup.Path, "*.tmp", SearchOption.TopDirectoryOnly)
                ),
            () => Assert.Equal(originalBytes, survivingBytes),
            () => Assert.Equal(2, survivor!.Files.Count)
        );
    }

    [Fact]
    internal async Task SaveChunkManifestAsync_AtomicWriteFails_DoesNotCreateManifest()
    {
        await using var provider = TestHost.CreateProvider();
        var encryptionServiceFactory = provider.GetRequiredService<IEncryptionServiceFactory>();

        using var backup = new TempDir();

        var failingFileOperations = Substitute.For<IFileOperationsService>();
        _ = failingFileOperations
            .WriteFileAtomicallyAsync(
                Arg.Any<string>(),
                Arg.Any<Func<Stream, CancellationToken, Task>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromException(new IOException("injected atomic write failure")));

        var failingService = new ManifestService(failingFileOperations, encryptionServiceFactory);

        var errors = await failingService.SaveChunkManifestAsync(
            NewManifest(2),
            backup.Path,
            RandomNumberGenerator.GetBytes(KeyLength),
            EncryptionAlgorithm.Aes,
            CancellationToken.None
        );

        Assert.Multiple(
            () => Assert.Single(errors),
            () => Assert.Equal(MessageCode.ManifestWriteFailedFormat, errors[0].Code),
            () => Assert.Contains<object>("injected atomic write failure", errors[0].Args),
            () => Assert.False(
                File.Exists(Path.Combine(backup.Path, BackupConstants.ManifestFileName)),
                "a save whose rename never happened must not leave a manifest at the destination"
            )
        );
    }

    [Theory]
    [MemberData(nameof(MalformedDocumentMasterSalts))]
    internal async Task DecryptChunkManifest_DocumentMasterSaltIsNotDecodable_ReturnsNull(string documentMasterSalt)
    {
        await using var provider = TestHost.CreateProvider();
        var manifestService = provider.GetRequiredService<IManifestService>();
        var encryptionServiceFactory = provider.GetRequiredService<IEncryptionServiceFactory>();

        var key = RandomNumberGenerator.GetBytes(KeyLength);
        var preambleSalt = RandomNumberGenerator.GetBytes(EncryptionConstants.SaltSize);

        var crafted = BuildCraftedPreamble(encryptionServiceFactory, key, preambleSalt, documentMasterSalt);

        Assert.Null(manifestService.DecryptChunkManifest(crafted, key));
    }

    [Fact]
    internal async Task ReadChunkManifestPreambleAsync_CancelledDuringRead_RethrowsInsteadOfReportingNoManifest()
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

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => manifestService.ReadChunkManifestPreambleAsync(backup.Path, cancellation.Token)
        );

        var afterwards = await manifestService.ReadChunkManifestPreambleAsync(backup.Path, CancellationToken.None);

        Assert.NotNull(afterwards);
    }

    [Fact]
    internal async Task DetectManifestKindAsync_ManifestReadFailsMidStream_ReportsMissingAndStillClosesTheStream()
    {
        await using var stream = new FailingReadStream();
        var fileOperations = Substitute.For<IFileOperationsService>();
        _ = fileOperations.DirectoryExists(Arg.Any<string>()).Returns(true);
        _ = fileOperations.FileExists(Arg.Any<string>()).Returns(true);
        _ = fileOperations
            .CombinePath(Arg.Any<string[]>())
            .Returns(call => Path.Combine(call.Arg<string[]>() ?? []));
        _ = fileOperations.OpenReadStream(Arg.Any<string>(), Arg.Any<int>()).Returns(stream);

        var manifestService = new ManifestService(fileOperations, Substitute.For<IEncryptionServiceFactory>());

        var kind = await manifestService.DetectManifestKindAsync(
            Path.Combine(Path.GetTempPath(), "bzc-midread"),
            CancellationToken.None
        );

        Assert.Multiple(
            () => Assert.Equal(ManifestKind.Missing, kind),
            () => Assert.True(
                stream.WasDisposed,
                "a manifest read that fails part way through must still release the handle it opened"
            )
        );
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
