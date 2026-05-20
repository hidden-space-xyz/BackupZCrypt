using BackupZCrypt.Application.Resources;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects.Manifest;
using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Services.Interfaces;
using BackupZCrypt.Domain.Strategies.Interfaces;

using System.Security.Cryptography;
using System.Text.Json;

namespace BackupZCrypt.Application.Services;

internal sealed class ManifestService(
    IFileOperationsService fileOperationsService,
    IEnumerable<IEncryptionAlgorithmStrategy> encryptionStrategies) : IManifestService
{
    private const int ChunkPreambleHeaderSize = 34;
    private const int NonceSize = 12;
    private const int MinChunkManifestSize = ChunkPreambleHeaderSize + NonceSize + 1;
    private readonly Dictionary<EncryptionAlgorithm, IEncryptionAlgorithmStrategy> encryptionStrategiesById =
        encryptionStrategies.ToDictionary(static strategy => strategy.Id, static strategy => strategy);

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
                header.Compression,
                [.. entries]);

            var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(document);
            var manifestPath = Path.Combine(destinationRoot, BackupConstants.ManifestFileName);
            await fileOperationsService.WriteAllBytesAsync(
                manifestPath, manifestBytes, cancellationToken);
        }
        catch (Exception ex)
        {
            errors.Add(string.Format(Messages.ManifestWriteFailedFormat, ex.Message));
        }

        return errors;
    }

    public async Task<ManifestPreamble?> ReadChunkManifestPreambleAsync(
        string sourceRoot,
        CancellationToken cancellationToken)
    {
        try
        {
            var manifestPath = Path.Combine(sourceRoot, BackupConstants.ManifestFileName);
            if (!fileOperationsService.FileExists(manifestPath))
            {
                return null;
            }

            var rawFile = await fileOperationsService.ReadAllBytesAsync(
                manifestPath,
                cancellationToken);
            if (rawFile.Length < MinChunkManifestSize)
            {
                return null;
            }

            return new ManifestPreamble(
                (EncryptionAlgorithm)rawFile[0],
                (KeyDerivationAlgorithm)rawFile[1],
                rawFile.AsSpan(2, ChunkPreambleHeaderSize - 2).ToArray(),
                rawFile.AsSpan(ChunkPreambleHeaderSize, NonceSize).ToArray(),
                rawFile.AsSpan(ChunkPreambleHeaderSize + NonceSize).ToArray());
        }
        catch
        {
            return null;
        }
    }

    public ChunkManifestData? DecryptChunkManifest(
        ManifestPreamble preamble,
        byte[] encryptionKey)
    {
        byte[]? plaintext = null;

        try
        {
            var encryptionStrategy = ResolveEncryptionStrategy(preamble.Algorithm);
            var associatedData = BuildChunkPreambleHeader(
                preamble.Algorithm,
                preamble.KeyDerivation,
                preamble.MasterSalt);
            plaintext = encryptionStrategy.DecryptChunk(
                preamble.EncryptedPayload,
                encryptionKey,
                preamble.Nonce,
                associatedData);

            var document = JsonSerializer.Deserialize<ChunkManifestDocument>(plaintext);
            var expectedMasterSalt = Convert.ToBase64String(preamble.MasterSalt);
            if (document is null
                || document.EncryptionAlgorithm != preamble.Algorithm
                || document.KeyDerivationAlgorithm != preamble.KeyDerivation
                || !string.Equals(document.MasterSalt, expectedMasterSalt, StringComparison.Ordinal))
            {
                return null;
            }

            return ToChunkManifestData(document);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (plaintext is not null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
    }

    public async Task<IReadOnlyList<string>> SaveChunkManifestAsync(
        ChunkManifestData manifestData,
        string destinationRoot,
        byte[] encryptionKey,
        EncryptionAlgorithm algorithm,
        CancellationToken cancellationToken)
    {
        List<string> errors = [];
        byte[]? manifestBytes = null;

        try
        {
            var masterSalt = Convert.FromBase64String(manifestData.MasterSalt);
            if (masterSalt.Length != ChunkPreambleHeaderSize - 2)
            {
                throw new FormatException("Manifest master salt must be 32 bytes.");
            }

            ChunkManifestDocument document = new(
                algorithm,
                manifestData.Header.KeyDerivationAlgorithm,
                manifestData.Header.Compression,
                manifestData.MasterSalt,
                manifestData.Files
                    .Select(f => new ChunkManifestFileEntrySerialized(
                        f.OriginalPath,
                        f.FileHash,
                        f.TotalSize,
                        [.. f.Chunks]))
                    .ToList());

            manifestBytes = JsonSerializer.SerializeToUtf8Bytes(document);

            var nonce = new byte[NonceSize];
            RandomNumberGenerator.Fill(nonce);

            var preambleHeader = BuildChunkPreambleHeader(
                algorithm,
                manifestData.Header.KeyDerivationAlgorithm,
                masterSalt);
            var encryptionStrategy = ResolveEncryptionStrategy(algorithm);
            var encryptedBytes = encryptionStrategy.EncryptChunk(
                manifestBytes,
                encryptionKey,
                nonce,
                preambleHeader);

            var payload = new byte[ChunkPreambleHeaderSize + NonceSize + encryptedBytes.Length];
            preambleHeader.CopyTo(payload, 0);
            nonce.CopyTo(payload, ChunkPreambleHeaderSize);
            encryptedBytes.CopyTo(payload, ChunkPreambleHeaderSize + NonceSize);

            var manifestPath = Path.Combine(destinationRoot, BackupConstants.ManifestFileName);
            await fileOperationsService.WriteAllBytesAsync(
                manifestPath,
                payload,
                cancellationToken);
        }
        catch (Exception ex)
        {
            errors.Add(string.Format(Messages.ManifestWriteFailedFormat, ex.Message));
        }
        finally
        {
            if (manifestBytes is not null)
            {
                CryptographicOperations.ZeroMemory(manifestBytes);
            }
        }

        return errors;
    }

    private static byte[] BuildChunkPreambleHeader(
        EncryptionAlgorithm algorithm,
        KeyDerivationAlgorithm keyDerivation,
        byte[] masterSalt)
    {
        var preambleHeader = new byte[ChunkPreambleHeaderSize];
        preambleHeader[0] = (byte)algorithm;
        preambleHeader[1] = (byte)keyDerivation;
        masterSalt.CopyTo(preambleHeader, 2);
        return preambleHeader;
    }

    private static ChunkManifestData ToChunkManifestData(ChunkManifestDocument document)
    {
        ManifestHeader header = new(
            document.EncryptionAlgorithm,
            document.KeyDerivationAlgorithm,
            document.Compression);

        List<ChunkManifestFileEntry> files = document.Files
            .Select(f => new ChunkManifestFileEntry(
                f.OriginalPath,
                f.FileHash,
                f.TotalSize,
                [.. f.Chunks]))
            .ToList();

        return new ChunkManifestData(header, document.MasterSalt, files);
    }

    private IEncryptionAlgorithmStrategy ResolveEncryptionStrategy(
        EncryptionAlgorithm algorithm)
    {
        return !encryptionStrategiesById.TryGetValue(algorithm, out var strategy)
            ? throw new ArgumentOutOfRangeException(
                nameof(algorithm),
                string.Format(
                    BackupZCrypt.Domain.Resources.Messages.EncryptionAlgorithmNotRegisteredFormat,
                    algorithm))
            : strategy;
    }
}
