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
    private const int MasterSaltSize = ChunkPreambleHeaderSize - 2;
    private const int MinChunkManifestSize = ChunkPreambleHeaderSize + NonceSize + 1;

    // Safety guard against malformed manifests causing unbounded memory allocations.
    // Raise this only after confirming your production manifest sizes require it.
    private const long MaxChunkManifestFileSizeBytes = 512L * 1024L * 1024L;

    private static readonly JsonSerializerOptions ManifestJsonOptions = new(JsonSerializerDefaults.General)
    {
        MaxDepth = 128,
        WriteIndented = false,
    };

    private readonly Dictionary<EncryptionAlgorithm, IEncryptionAlgorithmStrategy> encryptionStrategiesById =
        encryptionStrategies.ToDictionary(static strategy => strategy.Id, static strategy => strategy);

    public async Task<IReadOnlyList<string>> TrySavePlainManifestAsync(
        IReadOnlyList<ManifestEntry> entries,
        ManifestHeader header,
        string destinationRoot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationRoot);

        List<string> errors = [];
        if (entries.Count == 0)
        {
            return errors;
        }

        byte[]? manifestBytes = null;

        try
        {
            ManifestDocument document = new(
                header.EncryptionAlgorithm,
                header.KeyDerivationAlgorithm,
                header.Compression,
                [.. entries]);

            manifestBytes = JsonSerializer.SerializeToUtf8Bytes(document, ManifestJsonOptions);
            var manifestPath = Path.Combine(destinationRoot, BackupConstants.ManifestFileName);

            await fileOperationsService.WriteAllBytesAsync(
                manifestPath,
                manifestBytes,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
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

    public async Task<ManifestPreamble?> ReadChunkManifestPreambleAsync(
        string sourceRoot,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);

        try
        {
            var manifestPath = Path.Combine(sourceRoot, BackupConstants.ManifestFileName);
            if (!fileOperationsService.FileExists(manifestPath))
            {
                return null;
            }

            var manifestSize = fileOperationsService.GetFileSize(manifestPath);
            if (manifestSize < MinChunkManifestSize || manifestSize > MaxChunkManifestFileSizeBytes)
            {
                return null;
            }

            var rawFile = await fileOperationsService.ReadAllBytesAsync(
                manifestPath,
                cancellationToken).ConfigureAwait(false);

            if (rawFile.Length < MinChunkManifestSize || rawFile.LongLength > MaxChunkManifestFileSizeBytes)
            {
                return null;
            }

            var algorithm = (EncryptionAlgorithm)rawFile[0];
            var keyDerivation = (KeyDerivationAlgorithm)rawFile[1];

            if (!Enum.IsDefined(algorithm) || !Enum.IsDefined(keyDerivation))
            {
                return null;
            }

            return new ManifestPreamble(
                algorithm,
                keyDerivation,
                rawFile.AsSpan(2, MasterSaltSize).ToArray(),
                rawFile.AsSpan(ChunkPreambleHeaderSize, NonceSize).ToArray(),
                rawFile.AsSpan(ChunkPreambleHeaderSize + NonceSize).ToArray());
        }
        catch (OperationCanceledException)
        {
            throw;
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
        ArgumentNullException.ThrowIfNull(preamble);
        ArgumentNullException.ThrowIfNull(encryptionKey);

        byte[]? plaintext = null;
        byte[]? documentMasterSalt = null;

        try
        {
            if (preamble.MasterSalt.Length != MasterSaltSize
                || preamble.Nonce.Length != NonceSize
                || preamble.EncryptedPayload.Length == 0
                || !Enum.IsDefined(preamble.Algorithm)
                || !Enum.IsDefined(preamble.KeyDerivation))
            {
                return null;
            }

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

            var document = JsonSerializer.Deserialize<ChunkManifestDocument>(
                plaintext,
                ManifestJsonOptions);

            if (document is null
                || document.EncryptionAlgorithm != preamble.Algorithm
                || document.KeyDerivationAlgorithm != preamble.KeyDerivation
                || !TryDecodeBase64(document.MasterSalt, MasterSaltSize, out documentMasterSalt)
                || !CryptographicOperations.FixedTimeEquals(
                    documentMasterSalt,
                    preamble.MasterSalt))
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

            if (documentMasterSalt is not null)
            {
                CryptographicOperations.ZeroMemory(documentMasterSalt);
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
        ArgumentNullException.ThrowIfNull(manifestData);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationRoot);
        ArgumentNullException.ThrowIfNull(encryptionKey);

        List<string> errors = [];
        byte[]? manifestBytes = null;
        byte[]? encryptedBytes = null;
        byte[]? payload = null;

        try
        {
            var masterSalt = Convert.FromBase64String(manifestData.MasterSalt);
            if (masterSalt.Length != MasterSaltSize)
            {
                throw new FormatException("Manifest master salt must be exactly 32 bytes.");
            }

            if (!Enum.IsDefined(algorithm)
                || !Enum.IsDefined(manifestData.Header.KeyDerivationAlgorithm)
                || !Enum.IsDefined(manifestData.Header.Compression))
            {
                throw new InvalidDataException("Manifest contains an unsupported algorithm identifier.");
            }

            ChunkManifestDocument document = new(
                algorithm,
                manifestData.Header.KeyDerivationAlgorithm,
                manifestData.Header.Compression,
                manifestData.MasterSalt,
                manifestData.Files
                    .OrderBy(static f => f.OriginalPath, StringComparer.Ordinal)
                    .Select(static f => new ChunkManifestFileEntrySerialized(
                        f.OriginalPath,
                        f.FileHash,
                        f.TotalSize,
                        [.. f.Chunks]))
                    .ToList());

            manifestBytes = JsonSerializer.SerializeToUtf8Bytes(document, ManifestJsonOptions);

            var nonce = new byte[NonceSize];
            RandomNumberGenerator.Fill(nonce);

            var preambleHeader = BuildChunkPreambleHeader(
                algorithm,
                manifestData.Header.KeyDerivationAlgorithm,
                masterSalt);

            var encryptionStrategy = ResolveEncryptionStrategy(algorithm);
            encryptedBytes = encryptionStrategy.EncryptChunk(
                manifestBytes,
                encryptionKey,
                nonce,
                preambleHeader);

            payload = new byte[ChunkPreambleHeaderSize + NonceSize + encryptedBytes.Length];
            preambleHeader.CopyTo(payload, 0);
            nonce.CopyTo(payload, ChunkPreambleHeaderSize);
            encryptedBytes.CopyTo(payload, ChunkPreambleHeaderSize + NonceSize);

            var manifestPath = Path.Combine(destinationRoot, BackupConstants.ManifestFileName);
            await fileOperationsService.WriteAllBytesAsync(
                manifestPath,
                payload,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            errors.Add(string.Format(Messages.ManifestWriteFailedFormat, ex.Message));
        }
        finally
        {
            if (manifestBytes is not null)
            {
                CryptographicOperations.ZeroMemory(manifestBytes);
            }

            if (encryptedBytes is not null)
            {
                CryptographicOperations.ZeroMemory(encryptedBytes);
            }

            if (payload is not null)
            {
                CryptographicOperations.ZeroMemory(payload);
            }
        }

        return errors;
    }

    private static byte[] BuildChunkPreambleHeader(
        EncryptionAlgorithm algorithm,
        KeyDerivationAlgorithm keyDerivation,
        byte[] masterSalt)
    {
        if (masterSalt.Length != MasterSaltSize)
        {
            throw new ArgumentException(
                "Manifest master salt must be exactly 32 bytes.",
                nameof(masterSalt));
        }

        var preambleHeader = new byte[ChunkPreambleHeaderSize];
        preambleHeader[0] = (byte)algorithm;
        preambleHeader[1] = (byte)keyDerivation;
        masterSalt.CopyTo(preambleHeader, 2);
        return preambleHeader;
    }

    private static bool TryDecodeBase64(
        string value,
        int expectedLength,
        out byte[] decoded)
    {
        decoded = [];

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            decoded = Convert.FromBase64String(value);
            return decoded.Length == expectedLength;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static ChunkManifestData ToChunkManifestData(ChunkManifestDocument document)
    {
        ManifestHeader header = new(
            document.EncryptionAlgorithm,
            document.KeyDerivationAlgorithm,
            document.Compression);

        List<ChunkManifestFileEntry> files = document.Files
            .Select(static f => new ChunkManifestFileEntry(
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
