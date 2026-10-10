using System.Security.Cryptography;

using BackupZCrypt.Application.ValueObjects.Manifest;
using BackupZCrypt.Domain.Archive;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Application.Services;

internal sealed partial class ChunkedBackupService
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
    private async Task<(OpenedBackup? Backup, LocalizableMessage? Error)> OpenBackupAsync(
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

    /// <summary>
    /// Resolves the full path of a chunk inside a backup's chunks directory.
    /// </summary>
    /// <param name="chunksDir">The directory encrypted chunk files live in.</param>
    /// <param name="namingKey">The chunk-naming sub-key.</param>
    /// <param name="chunkHash">The SHA-256 content hash of the chunk.</param>
    /// <returns>The absolute path of the chunk file.</returns>
    private static string ComputeChunkFilePath(string chunksDir, byte[] namingKey, byte[] chunkHash)
    {
        return Path.Join(
            chunksDir,
            ChunkCrypto.ComputeChunkFileName(namingKey, chunkHash)
        );
    }

    /// <summary>
    /// Decodes a Base64 value read from the manifest and enforces its exact decoded length.
    /// </summary>
    /// <remarks>
    /// Length is checked before the bytes reach any cryptographic primitive, and a buffer of the
    /// wrong size is zeroed before the failure is raised.
    /// </remarks>
    /// <param name="value">The Base64 text to decode.</param>
    /// <param name="expectedLength">The exact number of bytes the value must decode to.</param>
    /// <param name="errorMessage">The message carried by the exception when validation fails.</param>
    /// <returns>The decoded bytes.</returns>
    /// <exception cref="CryptographicException">The value is not valid Base64 or decodes to the wrong length.</exception>
    private static byte[] DecodeBase64FixedLength(
        string value,
        int expectedLength,
        string errorMessage
    )
    {
        byte[] decoded;

        try
        {
            decoded = Convert.FromBase64String(value);
        }
        catch (FormatException ex)
        {
            throw new CryptographicException(errorMessage, ex);
        }

        if (decoded.Length != expectedLength)
        {
            CryptographicOperations.ZeroMemory(decoded);
            throw new CryptographicException(errorMessage);
        }

        return decoded;
    }

    /// <summary>
    /// An existing backup whose manifest has been decrypted, holding the keys that opened it until it
    /// is disposed.
    /// </summary>
    /// <param name="preamble">The manifest preamble read from disk.</param>
    /// <param name="keys">The keys derived for the backup, owned by this instance.</param>
    /// <param name="manifest">The decrypted manifest.</param>
    private sealed class OpenedBackup(
        ManifestPreamble preamble,
        ArchiveKeys keys,
        ChunkManifestData manifest
        ) : IDisposable
    {
        /// <summary>
        /// Gets the manifest preamble read from disk.
        /// </summary>
        public ManifestPreamble Preamble { get; } = preamble;

        /// <summary>
        /// Gets the keys derived for the backup.
        /// </summary>
        public ArchiveKeys Keys { get; } = keys;

        /// <summary>
        /// Gets the decrypted manifest.
        /// </summary>
        public ChunkManifestData Manifest { get; } = manifest;

        /// <summary>
        /// Wipes the keys.
        /// </summary>
        public void Dispose()
        {
            this.Keys.Dispose();
        }
    }

    /// <summary>
    /// Carries the three chunk sub-keys and the two strategies one chunking pass runs under, so a
    /// single value reaches every chunk of every file instead of five parallel arguments.
    /// </summary>
    /// <remarks>
    /// The set holds the very arrays <see cref="ArchiveKeys"/> owns rather than copies of them, so
    /// it neither extends the lifetime of any key nor changes when one is wiped: the operation's
    /// <see cref="ArchiveKeys"/> is still the only owner and still zeroes all of them on dispose.
    /// </remarks>
    /// <param name="chunkEncryptionKey">The sub-key chunk contents are encrypted with.</param>
    /// <param name="chunkNonceKey">The sub-key per-chunk nonces are derived from.</param>
    /// <param name="namingKey">The sub-key chunk file names are derived from.</param>
    /// <param name="encryptionStrategy">The strategy used to encrypt chunks.</param>
    /// <param name="compressionStrategy">The strategy applied before encryption, or <see langword="null"/> to skip compression.</param>
    private sealed class ChunkCipherSet(
        byte[] chunkEncryptionKey,
        byte[] chunkNonceKey,
        byte[] namingKey,
        IEncryptionAlgorithmStrategy encryptionStrategy,
        ICompressionStrategy? compressionStrategy
        )
    {
        /// <summary>
        /// Gets the sub-key used to encrypt chunk contents.
        /// </summary>
        public byte[] ChunkEncryptionKey { get; } = chunkEncryptionKey;

        /// <summary>
        /// Gets the sub-key each chunk's deterministic nonce is derived from.
        /// </summary>
        public byte[] ChunkNonceKey { get; } = chunkNonceKey;

        /// <summary>
        /// Gets the sub-key each chunk's on-disk file name is derived from.
        /// </summary>
        public byte[] NamingKey { get; } = namingKey;

        /// <summary>
        /// Gets the strategy used to encrypt chunks.
        /// </summary>
        public IEncryptionAlgorithmStrategy EncryptionStrategy { get; } = encryptionStrategy;

        /// <summary>
        /// Gets the strategy applied before encryption, or <see langword="null"/> when compression is disabled.
        /// </summary>
        public ICompressionStrategy? CompressionStrategy { get; } = compressionStrategy;
    }
}
