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
    /// Determines the on-disk format of the manifest associated with a backup path.
    /// </summary>
    /// <param name="backupPath">A path to the backup directory or a file within it.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The detected manifest kind, or <see cref="ManifestKind.Missing"/> if none is found.</returns>
    Task<ManifestKind> DetectManifestKindAsync(
        string backupPath,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Writes a plain (non-chunked) JSON manifest atomically to the destination root.
    /// </summary>
    /// <param name="entries">The per-file manifest entries to persist.</param>
    /// <param name="header">The algorithm and compression metadata to record.</param>
    /// <param name="destinationRoot">The backup root directory the manifest is written into.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>Localizable errors if the write failed; empty on success or when there are no entries.</returns>
    Task<IReadOnlyList<LocalizableMessage>> TrySavePlainManifestAsync(
        IReadOnlyList<ManifestEntry> entries,
        ManifestHeader header,
        string destinationRoot,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Reads the unencrypted preamble of a chunked manifest, exposing the parameters needed to derive keys.
    /// </summary>
    /// <param name="sourceRoot">The backup root directory containing the manifest.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The parsed preamble, or <see langword="null"/> if the manifest is missing or malformed.</returns>
    Task<ManifestPreamble?> ReadChunkManifestPreambleAsync(
        string sourceRoot,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Decrypts and validates a chunked manifest payload using the derived manifest key.
    /// </summary>
    /// <param name="preamble">The manifest preamble previously read from disk.</param>
    /// <param name="encryptionKey">The derived manifest encryption key.</param>
    /// <returns>The decrypted manifest data, or <see langword="null"/> if decryption or validation failed.</returns>
    ChunkManifestData? DecryptChunkManifest(ManifestPreamble preamble, byte[] encryptionKey);

    /// <summary>
    /// Encrypts and writes a chunked manifest atomically to the destination root.
    /// </summary>
    /// <param name="manifestData">The manifest contents to serialize and encrypt.</param>
    /// <param name="destinationRoot">The backup root directory the manifest is written into.</param>
    /// <param name="encryptionKey">The derived manifest encryption key.</param>
    /// <param name="algorithm">The encryption algorithm used to protect the manifest.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>Localizable errors if the write failed; empty on success.</returns>
    Task<IReadOnlyList<LocalizableMessage>> SaveChunkManifestAsync(
        ChunkManifestData manifestData,
        string destinationRoot,
        byte[] encryptionKey,
        EncryptionAlgorithm algorithm,
        CancellationToken cancellationToken
    );
}
