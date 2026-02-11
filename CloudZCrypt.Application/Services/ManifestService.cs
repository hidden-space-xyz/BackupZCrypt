using System.Text;
using System.Text.Json;
using CloudZCrypt.Application.Resources;
using CloudZCrypt.Application.Services.Interfaces;
using CloudZCrypt.Application.ValueObjects.Manifest;
using CloudZCrypt.Domain.Strategies.Interfaces;
using CloudZCrypt.Domain.ValueObjects.FileCrypt;

namespace CloudZCrypt.Application.Services;

internal sealed class ManifestService : IManifestService
{
    private static string AppFileExtension => ".czc";
    private static string ManifestFileName => "manifest" + AppFileExtension;

    public async Task<Dictionary<string, string>?> TryReadManifestAsync(
        string sourceRoot,
        IEncryptionAlgorithmStrategy encryptionService,
        FileCryptRequest request,
        CancellationToken cancellationToken
    )
    {
        try
        {
            string encryptedManifestPath = Path.Combine(sourceRoot, ManifestFileName);
            if (!File.Exists(encryptedManifestPath))
            {
                return null;
            }

            string tempJsonPath = Path.Combine(
                Path.GetTempPath(),
                $"czc-manifest-{Guid.NewGuid():N}.json"
            );
            bool ok = await encryptionService.DecryptFileAsync(
                encryptedManifestPath,
                tempJsonPath,
                request.Password,
                request.KeyDerivationAlgorithm
            );
            if (!ok)
            {
                try
                {
                    if (File.Exists(tempJsonPath))
                        File.Delete(tempJsonPath);
                }
                catch
                { /* ignore */
                }
                return null;
            }

            try
            {
                await using FileStream fs = File.OpenRead(tempJsonPath);
                List<ManifestEntry>? entries = await JsonSerializer.DeserializeAsync<
                    List<ManifestEntry>
                >(fs, cancellationToken: cancellationToken);
                Dictionary<string, string> map = new(StringComparer.OrdinalIgnoreCase);
                if (entries is not null)
                {
                    foreach (ManifestEntry e in entries)
                    {
                        map[e.ObfuscatedRelativePath] = e.OriginalRelativePath;
                    }
                }
                return map;
            }
            finally
            {
                try
                {
                    if (File.Exists(tempJsonPath))
                        File.Delete(tempJsonPath);
                }
                catch
                { /* ignore */
                }
            }
        }
        catch
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<string>> TrySaveManifestAsync(
        IReadOnlyList<ManifestEntry> entries,
        string destinationRoot,
        IEncryptionAlgorithmStrategy encryptionService,
        FileCryptRequest request,
        CancellationToken cancellationToken
    )
    {
        List<string> errors = [];
        if (entries.Count == 0)
        {
            return errors;
        }

        try
        {
            byte[] manifestBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entries));
            string encryptedManifestPath = Path.Combine(destinationRoot, ManifestFileName);
            bool manifestOk = await encryptionService.CreateEncryptedFileAsync(
                manifestBytes,
                encryptedManifestPath,
                request.Password,
                request.KeyDerivationAlgorithm
            );
            if (!manifestOk)
            {
                errors.Add(
                    string.Format(Messages.ManifestCreateFailedFormat, encryptedManifestPath)
                );
            }
        }
        catch (Exception ex)
        {
            errors.Add(string.Format(Messages.ManifestWriteFailedFormat, ex.Message));
        }

        return errors;
    }
}
