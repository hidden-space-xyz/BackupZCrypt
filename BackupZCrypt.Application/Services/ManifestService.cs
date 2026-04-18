namespace BackupZCrypt.Application.Services;

using BackupZCrypt.Application.Resources;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects.Manifest;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Backup;
using System.Text.Json;

internal sealed class ManifestService : IManifestService
{
    private const int PreambleSize = 2;

    private static string AppFileExtension => ".bzc";

    private static string ManifestFileName => "manifest" + AppFileExtension;

    public async Task<ManifestData?> TryReadManifestAsync(
        string sourceRoot,
        IReadOnlyList<IEncryptionAlgorithmStrategy> encryptionStrategies,
        string password,
        CancellationToken cancellationToken)
    {
        try
        {
            string encryptedManifestPath = Path.Combine(sourceRoot, ManifestFileName);
            if (!File.Exists(encryptedManifestPath))
            {
                return null;
            }

            byte[] rawFile = await File.ReadAllBytesAsync(
                encryptedManifestPath,
                cancellationToken);
            if (rawFile.Length < PreambleSize)
            {
                return null;
            }

            var algorithm = (EncryptionAlgorithm)rawFile[0];
            var kdf = (KeyDerivationAlgorithm)rawFile[1];

            IEncryptionAlgorithmStrategy? strategy = encryptionStrategies
                .FirstOrDefault(s => s.Id == algorithm);

            if (strategy is not null)
            {
                return await TryReadWithStrategyAsync(
                    rawFile.AsMemory(PreambleSize),
                    strategy,
                    password,
                    kdf,
                    cancellationToken);
            }

            return TryParsePlainManifest(rawFile);
        }
        catch
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<string>> TrySaveManifestAsync(
        IReadOnlyList<ManifestEntry> entries,
        ManifestHeader header,
        string destinationRoot,
        IEncryptionAlgorithmStrategy encryptionService,
        BackupRequest request,
        CancellationToken cancellationToken)
    {
        List<string> errors = [];
        if (entries.Count == 0)
        {
            return errors;
        }

        try
        {
            ManifestDocument document = new(
                header.EncryptionAlgorithm,
                header.KeyDerivationAlgorithm,
                header.NameObfuscation,
                header.Compression,
                [.. entries]);

            byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(document);
            string encryptedManifestPath = Path.Combine(destinationRoot, ManifestFileName);
            byte[] encryptedManifestBytes = await encryptionService.CreateEncryptedDataAsync(
                manifestBytes,
                request.Password,
                request.KeyDerivationAlgorithm,
                cancellationToken: cancellationToken);

            byte[] manifestPayload = new byte[PreambleSize + encryptedManifestBytes.Length];
            manifestPayload[0] = (byte)header.EncryptionAlgorithm;
            manifestPayload[1] = (byte)header.KeyDerivationAlgorithm;
            encryptedManifestBytes.CopyTo(manifestPayload, PreambleSize);

            await File.WriteAllBytesAsync(
                encryptedManifestPath,
                manifestPayload,
                cancellationToken);
        }
        catch (Exception ex)
        {
            errors.Add(string.Format(Messages.ManifestWriteFailedFormat, ex.Message));
        }

        return errors;
    }

    public async Task<IReadOnlyList<string>> TrySavePlainManifestAsync(
        IReadOnlyList<ManifestEntry> entries,
        ManifestHeader header,
        string destinationRoot,
        CancellationToken cancellationToken)
    {
        List<string> errors = [];
        if (entries.Count == 0)
        {
            return errors;
        }

        try
        {
            ManifestDocument document = new(
                header.EncryptionAlgorithm,
                header.KeyDerivationAlgorithm,
                header.NameObfuscation,
                header.Compression,
                [.. entries]);

            byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(document);
            string manifestPath = Path.Combine(destinationRoot, ManifestFileName);
            await File.WriteAllBytesAsync(manifestPath, manifestBytes, cancellationToken);
        }
        catch (Exception ex)
        {
            errors.Add(string.Format(Messages.ManifestWriteFailedFormat, ex.Message));
        }

        return errors;
    }

    private static async Task<ManifestData?> TryReadWithStrategyAsync(
        ReadOnlyMemory<byte> encryptedContent,
        IEncryptionAlgorithmStrategy encryptionService,
        string password,
        KeyDerivationAlgorithm kdf,
        CancellationToken cancellationToken)
    {
        try
        {
            byte[] plaintext = await encryptionService.ReadEncryptedDataAsync(
                encryptedContent,
                password,
                kdf,
                cancellationToken);

            ManifestDocument? doc = JsonSerializer.Deserialize<ManifestDocument>(plaintext);

            if (doc is null)
            {
                return null;
            }

            ManifestHeader header = new(
                doc.EncryptionAlgorithm,
                doc.KeyDerivationAlgorithm,
                doc.NameObfuscation,
                doc.Compression);

            Dictionary<string, ManifestFileInfo> map = new(StringComparer.OrdinalIgnoreCase);
            foreach (ManifestEntry e in doc.Entries)
            {
                byte[] salt = Convert.FromBase64String(e.Salt);
                byte[] nonce = Convert.FromBase64String(e.Nonce);
                map[e.RelativePath] = new ManifestFileInfo(
                    e.OriginalRelativePath,
                    salt,
                    nonce,
                    e.SourceHash);
            }

            return new ManifestData(header, map);
        }
        catch
        {
            return null;
        }
    }

    private static ManifestData? TryParsePlainManifest(byte[] rawFile)
    {
        try
        {
            ManifestDocument? doc = JsonSerializer.Deserialize<ManifestDocument>(rawFile);
            if (doc is null)
            {
                return null;
            }

            ManifestHeader header = new(
                doc.EncryptionAlgorithm,
                doc.KeyDerivationAlgorithm,
                doc.NameObfuscation,
                doc.Compression);

            Dictionary<string, ManifestFileInfo> map = new(StringComparer.OrdinalIgnoreCase);
            foreach (ManifestEntry e in doc.Entries)
            {
                byte[] salt = string.IsNullOrEmpty(e.Salt)
                    ? []
                    : Convert.FromBase64String(e.Salt);
                byte[] nonce = string.IsNullOrEmpty(e.Nonce)
                    ? []
                    : Convert.FromBase64String(e.Nonce);
                map[e.RelativePath] = new ManifestFileInfo(
                    e.OriginalRelativePath,
                    salt,
                    nonce,
                    e.SourceHash);
            }

            return new ManifestData(header, map);
        }
        catch
        {
            return null;
        }
    }
}
