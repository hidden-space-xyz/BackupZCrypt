namespace BackupZCrypt.Application.Services;

using BackupZCrypt.Application.Resources;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects.Manifest;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Domain.ValueObjects.FileCrypt;
using System.Text;
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
            else
            {
                await PrependPreambleAsync(
                    encryptedManifestPath,
                    header.EncryptionAlgorithm,
                    header.KeyDerivationAlgorithm,
                    cancellationToken);
            }
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

            byte[] manifestBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(document));
            string manifestPath = Path.Combine(destinationRoot, ManifestFileName);
            await File.WriteAllBytesAsync(manifestPath, manifestBytes, cancellationToken);
        }
        catch (Exception ex)
        {
            errors.Add(string.Format(Messages.ManifestWriteFailedFormat, ex.Message));
        }

        return errors;
    }

    private static async Task PrependPreambleAsync(
        string filePath,
        EncryptionAlgorithm algorithm,
        KeyDerivationAlgorithm kdf,
        CancellationToken cancellationToken)
    {
        byte[] encryptedContent = await File.ReadAllBytesAsync(filePath, cancellationToken);
        await using FileStream fs = new(filePath, FileMode.Create, FileAccess.Write);
        byte[] preamble = [(byte)algorithm, (byte)kdf];
        await fs.WriteAsync(preamble, cancellationToken);
        await fs.WriteAsync(encryptedContent, cancellationToken);
    }

    private static async Task<ManifestData?> TryReadWithStrategyAsync(
        ReadOnlyMemory<byte> encryptedContent,
        IEncryptionAlgorithmStrategy encryptionService,
        string password,
        KeyDerivationAlgorithm kdf,
        CancellationToken cancellationToken)
    {
        string tempEncryptedPath = Path.Combine(
            Path.GetTempPath(),
            $"bzc-manifest-{Guid.NewGuid():N}.bzc");

        try
        {
            await File.WriteAllBytesAsync(
                tempEncryptedPath,
                encryptedContent.ToArray(),
                cancellationToken);

            byte[] plaintext = await encryptionService.ReadEncryptedFileAsync(
                tempEncryptedPath,
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
                    nonce);
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
                if (File.Exists(tempEncryptedPath))
                {
                    File.Delete(tempEncryptedPath);
                }
            }
            catch
            { /* ignore */
            }
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
                    nonce);
            }

            return new ManifestData(header, map);
        }
        catch
        {
            return null;
        }
    }
}
