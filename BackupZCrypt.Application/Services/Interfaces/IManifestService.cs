using BackupZCrypt.Application.ValueObjects.Manifest;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Application.Services.Interfaces;

/// <summary>
/// Reads, writes, and decrypts backup manifests, which store the metadata required to restore a backup.
/// </summary>
public interface IManifestService
{
    /// <summary>
    /// Classifies the location a user picked as a backup folder.
    /// </summary>
    /// <param name="backupPath">The path the user picked as the backup folder.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The detected manifest kind.</returns>
    public Task<ManifestKind> DetectManifestKindAsync(
        string backupPath,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Inspects the manifest of an existing backup folder without decrypting it.
    /// </summary>
    /// <param name="backupRoot">The backup folder.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>Whether the manifest is missing, damaged, unsupported, or ready to be opened.</returns>
    public Task<ManifestKind> InspectManifestAsync(
        string backupRoot,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Reads the unencrypted preamble of a chunked manifest, exposing the parameters needed to derive keys.
    /// </summary>
    /// <param name="sourceRoot">The backup root directory containing the manifest.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The parsed preamble, or <see langword="null"/> if the manifest is missing or malformed.</returns>
    public Task<ManifestPreamble?> ReadChunkManifestPreambleAsync(
        string sourceRoot,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Decrypts and validates a chunked manifest payload using the derived manifest key.
    /// </summary>
    /// <param name="preamble">The manifest preamble previously read from disk.</param>
    /// <param name="encryptionKey">The derived manifest encryption key.</param>
    /// <returns>The decrypted manifest data, or <see langword="null"/> if decryption or validation failed.</returns>
    public ChunkManifestData? DecryptChunkManifest(ManifestPreamble preamble, byte[] encryptionKey);

    /// <summary>
    /// Encrypts and writes a chunked manifest atomically to the destination root.
    /// </summary>
    /// <param name="manifestData">The manifest contents to serialize and encrypt.</param>
    /// <param name="destinationRoot">The backup root directory the manifest is written into.</param>
    /// <param name="encryptionKey">The derived manifest encryption key.</param>
    /// <param name="algorithm">The encryption algorithm used to protect the manifest.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>Localizable errors if the write failed; empty on success.</returns>
    public Task<IReadOnlyList<LocalizableMessage>> SaveChunkManifestAsync(
        ChunkManifestData manifestData,
        string destinationRoot,
        byte[] encryptionKey,
        EncryptionAlgorithm algorithm,
        CancellationToken cancellationToken
    );
}
