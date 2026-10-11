using BackupZCrypt.Application.Algorithms;
using BackupZCrypt.Application.Common;
using BackupZCrypt.Application.Manifest;
using BackupZCrypt.Domain.Archive;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;

namespace BackupZCrypt.Application.Engine;

/// <summary>
/// Opens the existing backup an update, restore, verify, or preview works on, and explains why a
/// folder cannot be opened when it cannot.
/// </summary>
/// <param name="manifestService">The service used to read and decrypt the manifest.</param>
/// <param name="keyDerivationStrategies">The registered key derivation strategies, looked up by algorithm.</param>
internal sealed class BackupOpener(
    IManifestService manifestService,
    IStrategyRegistry<KeyDerivationAlgorithm, IKeyDerivationAlgorithmStrategy> keyDerivationStrategies
)
{
    /// <summary>
    /// Opens an existing backup: reads its preamble, derives its keys, and decrypts its manifest.
    /// </summary>
    /// <remarks>
    /// The password is normalized exactly as it was when the backup was created, so the same
    /// characters open it whichever Unicode form the keyboard, input method, or operating system
    /// produced.
    /// </remarks>
    /// <param name="backupRoot">The backup folder.</param>
    /// <param name="password">The password as the user typed it.</param>
    /// <param name="missingManifestCode">The message reported when the folder holds no manifest.</param>
    /// <param name="wrongPasswordCode">The message reported when the password does not open it.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The opened backup, or the error explaining why it could not be opened.</returns>
    internal async Task<(OpenedBackup? Backup, LocalizableMessage? Error)> OpenAsync(
        string backupRoot,
        string password,
        MessageCode missingManifestCode,
        MessageCode wrongPasswordCode,
        CancellationToken cancellationToken
    )
    {
        var preamble = await manifestService
            .ReadChunkManifestPreambleAsync(backupRoot, cancellationToken)
            .ConfigureAwait(false);

        if (preamble is null)
        {
            var kind = await manifestService
                .InspectManifestAsync(backupRoot, cancellationToken)
                .ConfigureAwait(false);

            return (
                null,
                new LocalizableMessage(
                    kind switch
                    {
                        ManifestKind.Missing or ManifestKind.PathNotFound or ManifestKind.NotADirectory =>
                            missingManifestCode,
                        ManifestKind.Unsupported => MessageCode.ManifestUnsupported,
                        ManifestKind.Encrypted or ManifestKind.Damaged => MessageCode.ManifestDamaged,
                        _ => MessageCode.ManifestDamaged,
                    }
                )
            );
        }

        cancellationToken.ThrowIfCancellationRequested();

        var keys = ArchiveKeys.Derive(
            password,
            preamble.MasterSalt,
            keyDerivationStrategies.Resolve(preamble.KeyDerivation)
        );
        var manifest = manifestService.DecryptChunkManifest(preamble, keys.ManifestEncryptionKey);

        if (manifest is null)
        {
            keys.Dispose();
            return (null, new LocalizableMessage(wrongPasswordCode));
        }

        return (new OpenedBackup(preamble, keys, manifest), null);
    }
}
