using BackupZCrypt.Application.ValueObjects.Manifest;
using BackupZCrypt.Domain.Enums;

namespace BackupZCrypt.Application.Services.Interfaces;

public interface IManifestService
{
    Task<IReadOnlyList<string>> TrySavePlainManifestAsync(
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

    Task<IReadOnlyList<string>> SaveChunkManifestAsync(
        ChunkManifestData manifestData,
        string destinationRoot,
        byte[] encryptionKey,
        EncryptionAlgorithm algorithm,
        CancellationToken cancellationToken
    );
}
