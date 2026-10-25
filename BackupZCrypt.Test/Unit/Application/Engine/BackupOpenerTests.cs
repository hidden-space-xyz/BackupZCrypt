using System.Security.Cryptography;

using BackupZCrypt.Application.Algorithms;
using BackupZCrypt.Application.Common;
using BackupZCrypt.Application.Engine;
using BackupZCrypt.Application.Manifest;
using BackupZCrypt.Domain.Algorithms;
using BackupZCrypt.Domain.Archive;

using NSubstitute;

namespace BackupZCrypt.Test.Unit.Application.Engine;

/// <summary>
/// Unit tests for <see cref="BackupOpener"/>, which derives an archive's keys and must wipe them on
/// every path that does not hand them to the opened backup.
/// </summary>
public sealed class BackupOpenerTests
{
    /// <summary>
    /// The backup folder the opener is pointed at; only the substituted manifest service reads it.
    /// </summary>
    private const string BackupRoot = "backup";

    /// <summary>
    /// The password the keys are derived from.
    /// </summary>
    private const string Password = "Correct-Horse-Battery-Staple-42";

    /// <summary>
    /// The message the opener is told to report when the password does not open the manifest.
    /// </summary>
    private const MessageCode WrongPasswordCode = MessageCode.InvalidPassword;

    /// <summary>
    /// The preamble the substituted manifest service reads from the folder.
    /// </summary>
    private static readonly ManifestPreamble Preamble = new(
        EncryptionAlgorithm.Aes,
        KeyDerivationAlgorithm.PBKDF2,
        new byte[EncryptionConstants.SaltSize],
        new byte[EncryptionConstants.NonceSize],
        []
    );

    /// <summary>
    /// The manifest service whose decryption each case scripts.
    /// </summary>
    private readonly IManifestService manifestService = Substitute.For<IManifestService>();

    /// <summary>
    /// The manifest key the opener handed to the manifest service, captured to check what became of it.
    /// </summary>
    private byte[]? manifestKey;

    /// <summary>
    /// Initializes a new instance of the <see cref="BackupOpenerTests"/> class with a folder whose
    /// preamble reads successfully.
    /// </summary>
    public BackupOpenerTests()
    {
        _ = this.manifestService
            .ReadChunkManifestPreambleAsync(BackupRoot, Arg.Any<CancellationToken>())
            .Returns(Preamble);
    }

    [Fact]
    internal async Task Open_ManifestDecrypts_HandsAnEqualKeyScheduleToTheOpenedBackupAndWipesItsOwn()
    {
        ChunkManifestData manifest = new(
            new ManifestHeader(EncryptionAlgorithm.Aes, KeyDerivationAlgorithm.PBKDF2, CompressionMode.None),
            Convert.ToBase64String(Preamble.MasterSalt),
            []
        );
        byte[] keyWhenUsed = [];
        _ = this.manifestService
            .DecryptChunkManifest(Preamble, Arg.Any<byte[]>())
            .Returns(call =>
            {
                this.manifestKey = call.ArgAt<byte[]>(1);
                keyWhenUsed = [.. this.manifestKey];
                return manifest;
            });

        var (backup, error) = await this.OpenAsync();

        using (backup)
        {
            Assert.Multiple(
                () => Assert.Null(error),
                () => Assert.Same(manifest, backup?.Manifest),
                () => Assert.Contains(keyWhenUsed, static b => b != 0),
                () => Assert.Equal(keyWhenUsed, backup?.Keys.ManifestEncryptionKey),
                this.AssertManifestKeyWiped
            );
        }
    }

    [Fact]
    internal async Task Open_WrongPassword_ReportsTheGivenCodeAndWipesTheKeys()
    {
        _ = this.manifestService
            .DecryptChunkManifest(Preamble, Arg.Any<byte[]>())
            .Returns(call =>
            {
                this.manifestKey = call.ArgAt<byte[]>(1);
                return null;
            });

        var (backup, error) = await this.OpenAsync();

        Assert.Multiple(
            () => Assert.Null(backup),
            () => Assert.Equal(WrongPasswordCode, error?.Code),
            this.AssertManifestKeyWiped
        );
    }

    [Fact]
    internal async Task Open_ManifestDecryptionThrows_PropagatesAndWipesTheKeys()
    {
        _ = this.manifestService
            .DecryptChunkManifest(Preamble, Arg.Any<byte[]>())
            .Returns(call =>
            {
                this.manifestKey = call.ArgAt<byte[]>(1);
                throw new CryptographicException("injected failure");
            });

        _ = await Assert.ThrowsAsync<CryptographicException>(this.OpenAsync);

        this.AssertManifestKeyWiped();
    }

    /// <summary>
    /// Creates a key derivation registry whose only strategy returns a fresh random master key.
    /// </summary>
    /// <returns>The substitute registry.</returns>
    private static IStrategyRegistry<KeyDerivationAlgorithm, IKeyDerivationAlgorithmStrategy> CreateKeyDerivations()
    {
        var strategy = Substitute.For<IKeyDerivationAlgorithmStrategy>();
        _ = strategy
            .DeriveKey(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<int>())
            .Returns(_ => RandomNumberGenerator.GetBytes(EncryptionConstants.KeySize / 8));
        var registry = Substitute.For<IStrategyRegistry<KeyDerivationAlgorithm, IKeyDerivationAlgorithmStrategy>>();
        _ = registry.Resolve(KeyDerivationAlgorithm.PBKDF2).Returns(strategy);
        return registry;
    }

    /// <summary>
    /// Opens the backup folder with the scripted manifest service.
    /// </summary>
    /// <returns>The opened backup, or the error explaining why it could not be opened.</returns>
    private Task<(OpenedBackup? Backup, LocalizableMessage? Error)> OpenAsync()
    {
        BackupOpener opener = new(this.manifestService, CreateKeyDerivations());

        return opener.OpenAsync(
            BackupRoot,
            Password,
            MessageCode.ManifestRequiredForDecryption,
            WrongPasswordCode,
            CancellationToken.None
        );
    }

    /// <summary>
    /// Asserts that the opener handed a manifest key to the manifest service and wiped it afterwards.
    /// </summary>
    private void AssertManifestKeyWiped()
    {
        Assert.NotNull(this.manifestKey);
        Assert.All(this.manifestKey, static b => Assert.Equal(0, b));
    }
}
