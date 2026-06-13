using BackupZCrypt.Application.ValueObjects.Manifest;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Application.Services.Interfaces;

public interface IManifestService
{
    Task<ManifestKind> DetectManifestKindAsync(
        string backupPath,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyList<LocalizableMessage>> TrySavePlainManifestAsync(
        IReadOnlyList<ManifestEntry> entries,
        ManifestHeader header,
        string destinationRoot,
        CancellationToken cancellationToken
    );

    Task<ManifestPreamble?> ReadChunkManifestPreambleAsync(
        string sourceRoot,
        CancellationToken cancellationToken
    );

    ChunkManifestData? DecryptChunkManifest(ManifestPreamble preamble, byte[] encryptionKey);

    Task<IReadOnlyList<LocalizableMessage>> SaveChunkManifestAsync(
        ChunkManifestData manifestData,
        string destinationRoot,
        byte[] encryptionKey,
        EncryptionAlgorithm algorithm,
        CancellationToken cancellationToken
    );
}
