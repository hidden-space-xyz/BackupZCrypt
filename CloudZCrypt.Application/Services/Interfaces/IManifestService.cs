namespace CloudZCrypt.Application.Services.Interfaces;

using CloudZCrypt.Application.ValueObjects.Manifest;
using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Strategies.Interfaces;
using CloudZCrypt.Domain.ValueObjects.FileCrypt;

public interface IManifestService
{
    Task<ManifestData?> TryReadManifestAsync(
        string sourceRoot,
        IReadOnlyList<IEncryptionAlgorithmStrategy> encryptionStrategies,
        string password,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> TrySaveManifestAsync(
        IReadOnlyList<ManifestEntry> entries,
        ManifestHeader header,
        string destinationRoot,
        IEncryptionAlgorithmStrategy encryptionService,
        FileCryptRequest request,
        CancellationToken cancellationToken);
}
