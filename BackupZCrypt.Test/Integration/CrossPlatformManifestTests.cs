using System.Security.Cryptography;

using BackupZCrypt.Application.Commands;
using BackupZCrypt.Application.Commands.Interfaces;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Application.ValueObjects.Backup;
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
/// Integration tests pinning that an archive is portable between operating systems: a manifest
/// records entry paths with forward slashes whichever platform wrote it, a manifest entry recorded
/// with Windows separators — whoever wrote it — still restores into a real directory tree on every
/// platform, and a traversal path written in Windows notation is rejected on every platform rather
/// than only where <c>\</c> happens to be the host separator.
/// </summary>
/// <remarks>
/// Every case uses PBKDF2 and files of a few bytes, so the fixture costs one key derivation per
/// backup operation and nothing else.
/// </remarks>
public sealed class CrossPlatformManifestTests
{
    /// <summary>
    /// The password every backup in this fixture is created and restored with; long and varied
    /// enough to clear the validator's strength warnings.
    /// </summary>
    private const string Password = "Correct-Horse-Battery-Staple-42";

    [Fact]
    internal async Task CreateThenRestore_NestedTree_RecordsForwardSlashPathsAndRebuildsTheSameStructure()
    {
        await using var provider = TestHost.CreateProvider();
        var createHandler = provider.GetRequiredService<ICommandHandler<CreateBackupCommand, Result<BackupOutcome>>>();
        var restoreHandler = provider.GetRequiredService<ICommandHandler<RestoreBackupCommand, Result<BackupOutcome>>>();

        using var source = new TempDir();
        using var destination = new TempDir();
        using var restored = new TempDir();

        _ = source.WriteText("root.txt", "root");
        _ = source.WriteText(Path.Combine("docs", "notes.md"), "# notes");
        _ = source.WriteText(Path.Combine("docs", "sub", "deep.txt"), "deep");

        await CreateBackupAsync(createHandler, source.Path, destination.Path);

        var manifest = await ReadManifestAsync(provider, destination.Path);
        var manifestPaths = manifest.Files.Select(static f => f.OriginalPath).ToList();
        string[] expectedManifestPaths = ["root.txt", "docs/notes.md", "docs/sub/deep.txt"];

        Assert.Multiple(
            () =>
                Assert.DoesNotContain(
                    manifestPaths,
                    static p => p.Contains('\\', StringComparison.Ordinal)
                ),
            () => Assert.Equivalent(expectedManifestPaths, manifestPaths, strict: true)
        );

        await RestoreBackupAsync(restoreHandler, destination.Path, restored.Path);

        var restoredPaths = Directory
            .GetFiles(restored.Path, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(restored.Path, f).Replace('\\', '/'))
            .ToList();
        var deepContent = await File.ReadAllTextAsync(
            Path.Combine(restored.Path, "docs", "sub", "deep.txt"),
            TestContext.Current.CancellationToken
        );

        Assert.Multiple(
            () => Assert.Equivalent(manifestPaths, restoredPaths, strict: true),
            () => Assert.Equal("deep", deepContent)
        );
    }

    [Fact]
    internal async Task Restore_ForeignManifestEntryWithBackslashSeparators_RebuildsNestedDirectoriesNotAFlatName()
    {
        await using var provider = TestHost.CreateProvider();
        var createHandler = provider.GetRequiredService<ICommandHandler<CreateBackupCommand, Result<BackupOutcome>>>();
        var restoreHandler = provider.GetRequiredService<ICommandHandler<RestoreBackupCommand, Result<BackupOutcome>>>();

        using var source = new TempDir();
        using var destination = new TempDir();
        using var restored = new TempDir();

        _ = source.WriteText(Path.Combine("docs", "notes.md"), "# notes");

        await CreateBackupAsync(createHandler, source.Path, destination.Path);
        await RewriteManifestPathsAsync(provider, destination.Path, static _ => "docs\\notes.md");

        await RestoreBackupAsync(restoreHandler, destination.Path, restored.Path);

        var restoredFile = Path.Combine(restored.Path, "docs", "notes.md");
        var restoredNames = Directory
            .GetFiles(restored.Path, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetFileName(f))
            .ToList();
        string[] expectedRestoredNames = ["notes.md"];

        Assert.Multiple(
            () =>
                Assert.True(
                    File.Exists(restoredFile),
                    "An entry recorded with Windows separators must restore as a nested directory tree on every platform."
                ),
            () => Assert.Equivalent(expectedRestoredNames, restoredNames, strict: true)
        );

        Assert.Equal(
            "# notes",
            await File.ReadAllTextAsync(restoredFile, TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    internal async Task Restore_ManifestPathWithWindowsTraversal_IsRejectedAndWritesNothing()
    {
        await using var provider = TestHost.CreateProvider();
        var createHandler = provider.GetRequiredService<ICommandHandler<CreateBackupCommand, Result<BackupOutcome>>>();
        var restoreHandler = provider.GetRequiredService<ICommandHandler<RestoreBackupCommand, Result<BackupOutcome>>>();

        using var source = new TempDir();
        using var destination = new TempDir();
        using var restored = new TempDir();

        _ = source.WriteText("payload.txt", "payload");
        await CreateBackupAsync(createHandler, source.Path, destination.Path);

        var escapedName = "bzc-escaped-" + Guid.NewGuid().ToString("N") + ".txt";
        var escapeTarget = Path.Combine(Path.GetTempPath(), escapedName);
        await RewriteManifestPathsAsync(provider, destination.Path, _ => "..\\..\\" + escapedName);

        var result = await restoreHandler.HandleAsync(
            new RestoreBackupCommand(destination.Path, restored.Path, Password, ProceedOnWarnings: true)
            {
                Progress = new RecordingProgress<BackupStatus>(),
            },
            TestContext.Current.CancellationToken
        );

        Assert.Multiple(
            () =>
                Assert.False(
                    result.IsSuccess,
                    "A manifest path with Windows traversal notation was accepted."
                ),
            () =>
                Assert.Contains(
                    result.Errors,
                    static e => e.Code is MessageCode.UnexpectedErrorFormat
                ),
            () =>
                Assert.False(
                    File.Exists(escapeTarget),
                    "The restore wrote a file outside its destination root. A TempDir root sits two levels below "
                        + "the platform temp directory, so the two-level traversal in the rewritten manifest path "
                        + "lands exactly there, and the name is unique so a file leaked by an earlier run can "
                        + "never pass for an escape."
                ),
            () => Assert.Empty(Directory.GetFiles(restored.Path, "*", SearchOption.AllDirectories))
        );
    }

    /// <summary>
    /// Backs up a source tree with AES plus PBKDF2, without compression and proceeding past
    /// advisory warnings, and fails the test if creation did not succeed, so a later assertion
    /// never reports a missing archive as a portability defect.
    /// </summary>
    /// <param name="handler">The handler that executes the create command.</param>
    /// <param name="sourcePath">The tree to back up.</param>
    /// <param name="destinationPath">The directory the backup is written to.</param>
    /// <returns>A task that completes once the backup exists.</returns>
    private static async Task CreateBackupAsync(
        ICommandHandler<CreateBackupCommand, Result<BackupOutcome>> handler,
        string sourcePath,
        string destinationPath
    )
    {
        var result = await handler.HandleAsync(
            new CreateBackupCommand(
                sourcePath,
                destinationPath,
                Password,
                Password,
                EncryptionAlgorithm.Aes,
                KeyDerivationAlgorithm.PBKDF2,
                CompressionMode.None,
                ProceedOnWarnings: true
            )
            {
                Progress = new RecordingProgress<BackupStatus>(),
            }
        );

        Assert.True(result.IsSuccess && result.Value.Completion!.IsSuccess, "Backup creation failed.");
    }

    /// <summary>
    /// Restores a backup and fails the test if the restore did not fully succeed.
    /// </summary>
    /// <param name="handler">The handler that executes the restore command.</param>
    /// <param name="backupPath">The backup root to restore from.</param>
    /// <param name="destinationPath">The directory the files are reconstructed into.</param>
    /// <returns>A task that completes once the restore has finished.</returns>
    private static async Task RestoreBackupAsync(
        ICommandHandler<RestoreBackupCommand, Result<BackupOutcome>> handler,
        string backupPath,
        string destinationPath
    )
    {
        var result = await handler.HandleAsync(
            new RestoreBackupCommand(backupPath, destinationPath, Password, ProceedOnWarnings: true)
            {
                Progress = new RecordingProgress<BackupStatus>(),
            }
        );

        Assert.True(result.IsSuccess && result.Value.Completion!.IsSuccess, "Restore failed.");
    }

    /// <summary>
    /// Reads and decrypts the manifest of an existing backup so a test can inspect the entry paths
    /// exactly as they were persisted.
    /// </summary>
    /// <param name="provider">The provider holding the real manifest and key derivation services.</param>
    /// <param name="backupRoot">The backup root whose manifest is read.</param>
    /// <returns>The decrypted manifest contents.</returns>
    private static async Task<ChunkManifestData> ReadManifestAsync(
        ServiceProvider provider,
        string backupRoot
    )
    {
        var manifestService = provider.GetRequiredService<IManifestService>();
        var preamble = await ReadPreambleAsync(manifestService, backupRoot);
        var manifestKey = DeriveManifestKey(provider, preamble);

        try
        {
            var manifest = manifestService.DecryptChunkManifest(preamble, manifestKey);
            Assert.NotNull(manifest);
            return manifest;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(manifestKey);
        }
    }

    /// <summary>
    /// Rewrites every manifest entry path through <paramref name="rewritePath"/> and saves the
    /// manifest again through the real service.
    /// </summary>
    /// <remarks>
    /// Going through <see cref="IManifestService.SaveChunkManifestAsync"/> keeps the authentication
    /// tag and the echoed master salt valid, so the result is a genuine archive that only differs in
    /// the notation of its paths, rather than a corrupt file the reader would reject for the wrong
    /// reason.
    /// </remarks>
    /// <param name="provider">The provider holding the real manifest and key derivation services.</param>
    /// <param name="backupRoot">The backup root whose manifest is rewritten.</param>
    /// <param name="rewritePath">Maps an existing entry path to the one that replaces it.</param>
    /// <returns>A task that completes once the rewritten manifest has been saved.</returns>
    private static async Task RewriteManifestPathsAsync(
        ServiceProvider provider,
        string backupRoot,
        Func<string, string> rewritePath
    )
    {
        var manifestService = provider.GetRequiredService<IManifestService>();
        var preamble = await ReadPreambleAsync(manifestService, backupRoot);
        var manifestKey = DeriveManifestKey(provider, preamble);

        try
        {
            var manifest = manifestService.DecryptChunkManifest(preamble, manifestKey);
            Assert.NotNull(manifest);

            var renamedFiles = manifest
                .Files.Select(f => f with { OriginalPath = rewritePath(f.OriginalPath) })
                .ToList();

            var rewritten = manifest with { Files = renamedFiles };

            var errors = await manifestService.SaveChunkManifestAsync(
                rewritten,
                backupRoot,
                manifestKey,
                preamble.Algorithm,
                CancellationToken.None
            );

            Assert.Empty(errors);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(manifestKey);
        }
    }

    /// <summary>
    /// Reads the unencrypted manifest preamble and fails the test if the backup has none.
    /// </summary>
    /// <param name="manifestService">The service that parses the preamble.</param>
    /// <param name="backupRoot">The backup root whose manifest is read.</param>
    /// <returns>The parsed preamble.</returns>
    private static async Task<ManifestPreamble> ReadPreambleAsync(
        IManifestService manifestService,
        string backupRoot
    )
    {
        var preamble = await manifestService.ReadChunkManifestPreambleAsync(
            backupRoot,
            CancellationToken.None
        );

        Assert.NotNull(preamble);
        return preamble;
    }

    /// <summary>
    /// Derives the manifest encryption sub-key the same way the backup service does, so a test can
    /// read or rewrite a real manifest without reaching into the service's private key handling.
    /// </summary>
    /// <param name="provider">The provider holding the real key derivation factory.</param>
    /// <param name="preamble">The preamble carrying the master salt and key derivation algorithm.</param>
    /// <returns>The manifest encryption key; the caller wipes it.</returns>
    private static byte[] DeriveManifestKey(ServiceProvider provider, ManifestPreamble preamble)
    {
        var masterKey = provider
            .GetRequiredService<IKeyDerivationServiceFactory>()
            .Create(preamble.KeyDerivation)
            .DeriveKey(Password, preamble.MasterSalt, EncryptionConstants.KeySize);

        try
        {
            var manifestKey = new byte[EncryptionConstants.KeySize / 8];
            HKDF.Expand(HashAlgorithmName.SHA256, masterKey, manifestKey, "manifest-encryption"u8);
            return manifestKey;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(masterKey);
        }
    }
}
