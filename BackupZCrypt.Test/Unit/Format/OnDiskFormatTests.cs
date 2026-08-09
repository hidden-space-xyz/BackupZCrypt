using System.Security.Cryptography;
using System.Text;

using BackupZCrypt.Application.Commands;
using BackupZCrypt.Application.Commands.Interfaces;
using BackupZCrypt.Application.Queries;
using BackupZCrypt.Application.Queries.Interfaces;
using BackupZCrypt.Application.Utilities.Helpers;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Application.ValueObjects.Backup;
using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Factories.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Backup;
using BackupZCrypt.Test.Common;
using BackupZCrypt.Test.Tools;

using Microsoft.Extensions.DependencyInjection;

namespace BackupZCrypt.Test.Unit.Format;

/// <summary>
/// Pins the on-disk archive format against silent, irreversible change.
/// </summary>
/// <remarks>
/// <para>
/// There is no password recovery in this product: an archive that stops decrypting is permanently
/// lost data. Every other test in this suite writes an archive with the current build and reads it
/// back with the same build, so all of them keep passing when the format itself moves. These do not.
/// </para>
/// <para>
/// Two guards are combined. The <b>fixture archives</b> under <c>TestData/OnDiskFormatArchives</c> were
/// written by an earlier build and are committed as binary; restoring them exercises the whole
/// format end to end, so a change to the manifest JSON keys, the preamble layout, the HKDF labels,
/// the chunk-naming HMAC, the nonce derivation, or the compress-then-encrypt order breaks them. The
/// <b>golden vectors</b> pin the key schedule arithmetically, so a changed KDF parameter is caught
/// with a readable diff instead of an opaque "could not decrypt".
/// </para>
/// <para>
/// If one of these fails, the correct first assumption is that a change broke the format — not that
/// the test is stale. Regenerate the fixtures with <c>OnDiskFormatFixtureGenerator</c> only when the format
/// was changed deliberately and every existing archive is understood to be abandoned.
/// </para>
/// </remarks>
public sealed class OnDiskFormatTests
{
    /// <summary>
    /// The password every committed fixture archive was created with.
    /// </summary>
    private const string Password = "Correct-Horse-Battery-Staple-42";

    /// <summary>
    /// Gets the fixture archives, exposed as a theory data source so each gets its own result.
    /// </summary>
    /// <remarks>
    /// It is public only because <see cref="MemberDataAttribute"/> can reference nothing less
    /// visible; every other member of this class is as private as the runner allows.
    /// </remarks>
    public static TheoryData<OnDiskFormatFixture> Fixtures
    {
        get
        {
            var data = new TheoryData<OnDiskFormatFixture>();

            foreach (var fixture in OnDiskFormatFixtures.All)
            {
                data.Add(fixture);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    internal async Task Restore_CommittedFixtureArchive_RebuildsTheOriginalTreeByteForByte(
        OnDiskFormatFixture fixture
    )
    {
        var archive = RequireFixture(fixture);

        await using var provider = TestHost.CreateProvider();
        using var restored = new TempDir();

        var result = await provider
            .GetRequiredService<ICommandHandler<RestoreBackupCommand, Result<BackupOutcome>>>()
            .HandleAsync(
                new RestoreBackupCommand(archive, restored.Path, Password, ProceedOnWarnings: true)
                {
                    Progress = new RecordingProgress<BackupStatus>(),
                },
                CancellationToken.None
            );

        Assert.True(
            result.IsSuccess && result.Value.Completion!.IsSuccess,
            $"The committed '{fixture.Name}' archive no longer restores. The on-disk format changed; "
                + "every archive a user already wrote is unreadable by this build."
        );

        var restoredContents = new List<(string RelativePath, string Expected, string? Actual)>();

        foreach (var (relativePath, expected) in OnDiskFormatFixtures.SourceTree)
        {
            var restoredFile = Path.Combine(
                restored.Path,
                relativePath.Replace('/', Path.DirectorySeparatorChar)
            );
            var actual = File.Exists(restoredFile)
                ? await File.ReadAllTextAsync(
                    restoredFile,
                    Encoding.UTF8,
                    TestContext.Current.CancellationToken
                )
                : null;

            restoredContents.Add((relativePath, expected, actual));
        }

        Assert.Multiple(
            () =>
                Assert.All(
                    restoredContents,
                    entry =>
                    {
                        Assert.NotNull(entry.Actual);
                        Assert.Equal(entry.Expected, entry.Actual);
                    }
                ),
            () =>
                Assert.Equal(
                    OnDiskFormatFixtures.SourceTree.Length,
                    Directory.GetFiles(restored.Path, "*", SearchOption.AllDirectories).Length
                )
        );
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    internal async Task Verify_CommittedFixtureArchive_ReportsEveryChunkIntact(
        OnDiskFormatFixture fixture
    )
    {
        var archive = RequireFixture(fixture);

        await using var provider = TestHost.CreateProvider();

        var result = await provider
            .GetRequiredService<IQueryHandler<VerifyBackupQuery, Result<BackupOutcome>>>()
            .HandleAsync(
                new VerifyBackupQuery(archive, Password)
                {
                    Progress = new RecordingProgress<BackupStatus>(),
                },
                CancellationToken.None
            );

        Assert.True(
            result.IsSuccess && result.Value.Completion!.IsSuccess,
            $"Verification of the committed '{fixture.Name}' archive failed."
        );
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    internal void ManifestPreamble_OfCommittedFixture_KeepsThe34BytePlaintextLayout(
        OnDiskFormatFixture fixture
    )
    {
        var manifest = Path.Combine(RequireFixture(fixture), "manifest.bzc");
        var raw = File.ReadAllBytes(manifest);

        Assert.Multiple(
            () =>
                Assert.True(
                    raw.Length > 2 + EncryptionConstants.SaltSize + EncryptionConstants.NonceSize,
                    "The manifest is too short to hold a preamble, a nonce, and a payload."
                ),
            () => Assert.Equal((byte)fixture.Encryption, raw[0]),
            () => Assert.Equal((byte)fixture.KeyDerivation, raw[1])
        );
    }

    [Theory]
    [InlineData(
        KeyDerivationAlgorithm.Argon2id,
        "1574da42c67f0e9384fce03787cbbbe7ea38b5a4192cfc397dbc38619a63a2a2"
    )]
    [InlineData(
        KeyDerivationAlgorithm.PBKDF2,
        "bba95f7a23ed99c9039c8d56e52a481b8c7420c6ba4a9b15252c5bd3feeb1f79"
    )]
    [InlineData(
        KeyDerivationAlgorithm.Scrypt,
        "4bb6d1232ec1e5d3f1730dbb4d8b0097ad3a8a2de53f96368a24c815ea482d9f"
    )]
    internal void DeriveKey_WithTheGoldenPasswordAndSalt_ReturnsThePinnedMasterKey(
        KeyDerivationAlgorithm algorithm,
        string expectedHex
    )
    {
        using var provider = TestHost.CreateProvider();

        var masterKey = provider
            .GetRequiredService<IKeyDerivationServiceFactory>()
            .Create(algorithm)
            .DeriveKey(Password, OnDiskFormatFixtures.GoldenSalt, EncryptionConstants.KeySize);

        try
        {
            Assert.Equal(expectedHex, Convert.ToHexStringLower(masterKey));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(masterKey);
        }
    }

    [Theory]
    [InlineData("chunk-encryption", "c1e8b496572cfd4c474374db2a6a402910662ca5fb462be072a305d7591f645b")]
    [InlineData("chunk-nonce", "a1bdc141f016ae95a7395ce392848c8d35270675263cbfb5a8ffaa61fc3ea157")]
    [InlineData("chunk-naming", "928c856c5cef08c0b6ddb3fd15a0627696eedae2b918df900b7c449ff07037c2")]
    [InlineData(
        "manifest-encryption",
        "c32f4c44a4dfdde445949bd8087c58c7b9d9e1707ca4be1445aaf3603afdabc7"
    )]
    internal void ExpandSubKey_FromThePinnedPbkdf2MasterKey_ReturnsThePinnedSubKey(
        string label,
        string expectedHex
    )
    {
        Assert.Contains(label, OnDiskFormatFixtures.SubKeyLabels);

        var masterKey = Convert.FromHexString(
            "bba95f7a23ed99c9039c8d56e52a481b8c7420c6ba4a9b15252c5bd3feeb1f79"
        );
        var subKey = new byte[EncryptionConstants.KeySize / 8];

        try
        {
            HKDF.Expand(
                HashAlgorithmName.SHA256,
                masterKey,
                subKey,
                Encoding.UTF8.GetBytes(label)
            );

            Assert.Equal(expectedHex, Convert.ToHexStringLower(subKey));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(masterKey);
            CryptographicOperations.ZeroMemory(subKey);
        }
    }

    [Fact]
    internal void ComputeChunkNonce_WithThePinnedNonceKey_ReturnsThePinnedNonce()
    {
        var nonceKey = Convert.FromHexString(OnDiskFormatFixtures.GoldenChunkNonceKeyHex);
        var chunkHash = SHA256.HashData(OnDiskFormatFixtures.GoldenChunkContent);

        var nonce = ChunkCryptoHelper.ComputeChunkNonce(nonceKey, chunkHash);

        Assert.Multiple(
            () => Assert.Equal(EncryptionConstants.NonceSize, nonce.Length),
            () => Assert.Equal("ee5d7d9a1b9eb918308f8f59", Convert.ToHexStringLower(nonce))
        );
    }

    [Fact]
    internal void BuildChunkAssociatedData_PinsTheChunkHashThenNonceLayout()
    {
        var chunkHash = SHA256.HashData(OnDiskFormatFixtures.GoldenChunkContent);
        var nonce = ChunkCryptoHelper.ComputeChunkNonce(
            Convert.FromHexString(OnDiskFormatFixtures.GoldenChunkNonceKeyHex),
            chunkHash
        );

        var associatedData = ChunkCryptoHelper.BuildChunkAssociatedData(chunkHash, nonce);

        Assert.Equal(
            Convert.ToHexStringLower(chunkHash) + Convert.ToHexStringLower(nonce),
            Convert.ToHexStringLower(associatedData)
        );
    }

    [Fact]
    internal void ChunkFileName_WithThePinnedNamingKey_ReturnsThePinnedHmacHex()
    {
        var namingKey = Convert.FromHexString(OnDiskFormatFixtures.GoldenChunkNamingKeyHex);
        var chunkHash = SHA256.HashData(OnDiskFormatFixtures.GoldenChunkContent);

        var fileName = Convert.ToHexStringLower(HMACSHA256.HashData(namingKey, chunkHash));

        Assert.Equal("d35f87764839b54b3ef3105e52b2fd06516625c71677e5f120e4a417654fdb5d", fileName);
    }

    [Fact]
    internal void EncryptionConstants_KeepTheSizesEveryArchiveWasWrittenWith()
    {
        Assert.Multiple(
            () => Assert.Equal(256, EncryptionConstants.KeySize),
            () => Assert.Equal(32, EncryptionConstants.SaltSize),
            () => Assert.Equal(12, EncryptionConstants.NonceSize),
            () => Assert.Equal(128, EncryptionConstants.MacSize),
            () => Assert.Equal(16, EncryptionConstants.TagSize)
        );
    }

    /// <summary>
    /// Resolves a fixture's deployed directory, failing with a clear instruction when it is absent.
    /// </summary>
    /// <param name="fixture">The fixture to locate.</param>
    /// <returns>The absolute path of the deployed fixture archive.</returns>
    private static string RequireFixture(OnDiskFormatFixture fixture)
    {
        var path = Path.Combine(OnDiskFormatFixtures.DeployedTestDataRoot, fixture.Name);

        Assert.True(
            Directory.Exists(path) && File.Exists(Path.Combine(path, "manifest.bzc")),
            $"The '{fixture.Name}' fixture archive is missing from {path}. It is committed binary test "
                + "data; restore it from source control rather than regenerating it, or the format it "
                + "pins is lost."
        );

        return path;
    }
}
