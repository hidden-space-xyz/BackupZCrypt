namespace CloudZCrypt.Application.Services;

using System.Text;
using System.Text.Json;
using CloudZCrypt.Application.Resources;
using CloudZCrypt.Application.Services.Interfaces;
using CloudZCrypt.Application.ValueObjects.Manifest;
using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Strategies.Interfaces;
using CloudZCrypt.Domain.ValueObjects.FileCrypt;

internal sealed class ManifestService : IManifestService
{
    private static string AppFileExtension => ".czc";

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

            foreach (IEncryptionAlgorithmStrategy strategy in encryptionStrategies)
            {
                foreach (KeyDerivationAlgorithm kdf in Enum.GetValues<KeyDerivationAlgorithm>())
                {
                    ManifestData? result = await TryReadWithStrategyAsync(
                        encryptedManifestPath,
                        strategy,
                        password,
                        kdf,
                        cancellationToken);
                    if (result is not null)
                    {
                        return result;
                    }
                }
            }

            return null;
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
        FileCryptRequest request,
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

            byte[] manifestBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(document));
            string encryptedManifestPath = Path.Combine(destinationRoot, ManifestFileName);
            bool manifestOk = await encryptionService.CreateEncryptedFileAsync(
                manifestBytes,
                encryptedManifestPath,
                request.Password,
                request.KeyDerivationAlgorithm,
                cancellationToken: cancellationToken);
            if (!manifestOk)
            {
                errors.Add(
                    string.Format(Messages.ManifestCreateFailedFormat, encryptedManifestPath));
            }
        }
        catch (Exception ex)
        {
            errors.Add(string.Format(Messages.ManifestWriteFailedFormat, ex.Message));
        }

        return errors;
    }

    private static async Task<ManifestData?> TryReadWithStrategyAsync(
        string encryptedManifestPath,
        IEncryptionAlgorithmStrategy encryptionService,
        string password,
        KeyDerivationAlgorithm kdf,
        CancellationToken cancellationToken)
    {
        string tempJsonPath = Path.Combine(
            Path.GetTempPath(),
            $"czc-manifest-{Guid.NewGuid():N}.json");

        try
        {
            bool ok = await encryptionService.DecryptFileAsync(
                encryptedManifestPath,
                tempJsonPath,
                password,
                kdf,
                cancellationToken);
            if (!ok)
            {
                return null;
            }

            await using FileStream fs = File.OpenRead(tempJsonPath);
            ManifestDocument? doc = await JsonSerializer.DeserializeAsync<ManifestDocument>(
                fs,
                cancellationToken: cancellationToken);

            if (doc is null)
            {
                return null;
            }

            ManifestHeader header = new(
                doc.EncryptionAlgorithm,
                doc.KeyDerivationAlgorithm,
                doc.NameObfuscation,
                doc.Compression);

            Dictionary<string, string> map = new(StringComparer.OrdinalIgnoreCase);
            foreach (ManifestEntry e in doc.Entries)
            {
                map[e.ObfuscatedRelativePath] = e.OriginalRelativePath;
            }

            return new ManifestData(header, map);
        }
        catch
        {
            return null;
        }
        finally
        {
            try
            {
                if (File.Exists(tempJsonPath))
                {
                    File.Delete(tempJsonPath);
                }
            }
            catch
            { /* ignore */
            }
        }
    }
}
