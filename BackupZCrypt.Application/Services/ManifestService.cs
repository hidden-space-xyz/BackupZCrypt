using System.Security.Cryptography;
using System.Text.Json;

using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects.Manifest;
using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Services.Interfaces;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Application.Services;

/// <summary>
/// Reads, writes, decrypts, and classifies backup manifests. Chunked manifests are stored as an
/// unencrypted preamble (algorithm, key derivation, and master salt used as associated data)
/// followed by an AEAD-encrypted document, and are written atomically via a temp file and rename.
/// </summary>
/// <param name="fileOperationsService">Service used to read and write manifest files.</param>
/// <param name="encryptionStrategies">The available encryption strategies, indexed by their algorithm identifier.</param>
internal sealed class ManifestService(
    IFileOperationsService fileOperationsService,
    IEnumerable<IEncryptionAlgorithmStrategy> encryptionStrategies
) : IManifestService
{
    private const int ChunkPreambleHeaderSize = 34;

    private readonly Dictionary<
        EncryptionAlgorithm,
        IEncryptionAlgorithmStrategy
    > encryptionStrategiesById = encryptionStrategies.ToDictionary(
        static strategy => strategy.Id,
        static strategy => strategy
    );

    /// <summary>
    /// Determines whether a readable backup manifest is present: a non-empty manifest is reported as
    /// an encrypted backup, since encryption is the only supported format.
    /// </summary>
    /// <param name="backupPath">A path to the backup directory or a file within it.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The detected manifest kind, or <see cref="ManifestKind.Missing"/> if none is found or readable.</returns>
    /// <exception cref="ArgumentException"><paramref name="backupPath"/> is <see langword="null"/> or whitespace.</exception>
    public async Task<ManifestKind> DetectManifestKindAsync(
        string backupPath,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);

        try
        {
            var directory = fileOperationsService.DirectoryExists(backupPath)
                ? backupPath
                : fileOperationsService.GetDirectoryName(backupPath) ?? string.Empty;

            if (string.IsNullOrEmpty(directory))
            {
                return ManifestKind.Missing;
            }

            var manifestPath = fileOperationsService.CombinePath(
                directory,
                BackupConstants.ManifestFileName
            );

            if (!fileOperationsService.FileExists(manifestPath))
            {
                return ManifestKind.Missing;
            }

            var firstByte = new byte[1];

            await using var stream = fileOperationsService.OpenReadStream(
                manifestPath,
                bufferSize: 16
            );

            var read = await stream
                .ReadAsync(firstByte.AsMemory(0, 1), cancellationToken)
                .ConfigureAwait(false);

            return read == 0 ? ManifestKind.Missing : ManifestKind.Encrypted;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return ManifestKind.Missing;
        }
    }

    /// <summary>
    /// Reads and parses the unencrypted preamble of a chunked manifest, validating the algorithm identifiers.
    /// </summary>
    /// <param name="sourceRoot">The backup root directory containing the manifest.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The parsed preamble, or <see langword="null"/> if the manifest is missing, malformed, or unreadable.</returns>
    /// <exception cref="ArgumentException"><paramref name="sourceRoot"/> is <see langword="null"/> or whitespace.</exception>
    public async Task<ManifestPreamble?> ReadChunkManifestPreambleAsync(
        string sourceRoot,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);

        try
        {
            var manifestPath = Path.Combine(sourceRoot, BackupConstants.ManifestFileName);
            if (!fileOperationsService.FileExists(manifestPath))
            {
                return null;
            }

            var rawFile = await fileOperationsService
                .ReadAllBytesAsync(manifestPath, cancellationToken)
                .ConfigureAwait(false);

            var algorithm = (EncryptionAlgorithm)rawFile[0];
            var keyDerivation = (KeyDerivationAlgorithm)rawFile[1];

            return !Enum.IsDefined(algorithm) || !Enum.IsDefined(keyDerivation)
                ? null
                : new ManifestPreamble(
                algorithm,
                keyDerivation,
                rawFile.AsSpan(2, EncryptionConstants.SaltSize).ToArray(),
                rawFile.AsSpan(ChunkPreambleHeaderSize, EncryptionConstants.NonceSize).ToArray(),
                rawFile.AsSpan(ChunkPreambleHeaderSize + EncryptionConstants.NonceSize).ToArray()
            );
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

    /// <summary>
    /// Decrypts a chunked manifest payload and verifies that the embedded master salt matches the
    /// preamble in constant time, guarding against tampering.
    /// </summary>
    /// <param name="preamble">The manifest preamble previously read from disk.</param>
    /// <param name="encryptionKey">The derived manifest encryption key.</param>
    /// <returns>The decrypted manifest data, or <see langword="null"/> if decryption or validation failed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="preamble"/> or <paramref name="encryptionKey"/> is <see langword="null"/>.</exception>
    public ChunkManifestData? DecryptChunkManifest(ManifestPreamble preamble, byte[] encryptionKey)
    {
        ArgumentNullException.ThrowIfNull(preamble);
        ArgumentNullException.ThrowIfNull(encryptionKey);

        byte[]? plaintext = null;
        byte[]? documentMasterSalt = null;

        try
        {
            if (
                preamble.MasterSalt.Length != EncryptionConstants.SaltSize
                || preamble.Nonce.Length != EncryptionConstants.NonceSize
                || preamble.EncryptedPayload.Length == 0
                || !Enum.IsDefined(preamble.Algorithm)
                || !Enum.IsDefined(preamble.KeyDerivation)
            )
            {
                return null;
            }

            var encryptionStrategy = ResolveEncryptionStrategy(preamble.Algorithm);
            var associatedData = BuildChunkPreambleHeader(
                preamble.Algorithm,
                preamble.KeyDerivation,
                preamble.MasterSalt
            );

            plaintext = encryptionStrategy.DecryptChunk(
                preamble.EncryptedPayload,
                encryptionKey,
                preamble.Nonce,
                associatedData
            );

            var document = JsonSerializer.Deserialize<ChunkManifestDocument>(plaintext);

            return document is null
                || document.EncryptionAlgorithm != preamble.Algorithm
                || document.KeyDerivationAlgorithm != preamble.KeyDerivation
                || !TryDecodeBase64(
                    document.MasterSalt,
                    EncryptionConstants.SaltSize,
                    out documentMasterSalt
                )
                || !CryptographicOperations.FixedTimeEquals(documentMasterSalt, preamble.MasterSalt)
                ? null
                : ToChunkManifestData(document);
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

    /// <summary>
    /// Serializes, encrypts, and atomically writes a chunked manifest, prefixing it with the preamble
    /// header and a freshly generated nonce.
    /// </summary>
    /// <param name="manifestData">The manifest contents to serialize and encrypt.</param>
    /// <param name="destinationRoot">The backup root directory the manifest is written into.</param>
    /// <param name="encryptionKey">The derived manifest encryption key.</param>
    /// <param name="algorithm">The encryption algorithm used to protect the manifest.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>Localizable errors if the write failed; empty on success.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="manifestData"/> or <paramref name="encryptionKey"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="destinationRoot"/> is <see langword="null"/> or whitespace.</exception>
    public async Task<IReadOnlyList<LocalizableMessage>> SaveChunkManifestAsync(
        ChunkManifestData manifestData,
        string destinationRoot,
        byte[] encryptionKey,
        EncryptionAlgorithm algorithm,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(manifestData);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationRoot);
        ArgumentNullException.ThrowIfNull(encryptionKey);

        List<LocalizableMessage> errors = [];
        byte[]? manifestBytes = null;
        byte[]? encryptedBytes = null;
        byte[]? payload = null;

        try
        {
            var masterSalt = Convert.FromBase64String(manifestData.MasterSalt);
            if (masterSalt.Length != EncryptionConstants.SaltSize)
            {
                throw new FormatException("Manifest master salt must be exactly 32 bytes.");
            }

            if (
                !Enum.IsDefined(algorithm)
                || !Enum.IsDefined(manifestData.Header.KeyDerivationAlgorithm)
                || !Enum.IsDefined(manifestData.Header.Compression)
            )
            {
                throw new InvalidDataException(
                    "Manifest contains an unsupported algorithm identifier."
                );
            }

            ChunkManifestDocument document = new(
                algorithm,
                manifestData.Header.KeyDerivationAlgorithm,
                manifestData.Header.Compression,
                manifestData.MasterSalt,
                manifestData
                    .Files.OrderBy(static f => f.OriginalPath, StringComparer.Ordinal)
                    .Select(static f => new ChunkManifestFileEntrySerialized(
                        f.OriginalPath,
                        f.FileHash,
                        f.TotalSize,
                        [.. f.Chunks]
                    ))
                    .ToList()
            );

            manifestBytes = JsonSerializer.SerializeToUtf8Bytes(document);

            var nonce = new byte[EncryptionConstants.NonceSize];
            RandomNumberGenerator.Fill(nonce);

            var preambleHeader = BuildChunkPreambleHeader(
                algorithm,
                manifestData.Header.KeyDerivationAlgorithm,
                masterSalt
            );

            var encryptionStrategy = ResolveEncryptionStrategy(algorithm);
            encryptedBytes = encryptionStrategy.EncryptChunk(
                manifestBytes,
                encryptionKey,
                nonce,
                preambleHeader
            );

            payload = new byte[
                ChunkPreambleHeaderSize + EncryptionConstants.NonceSize + encryptedBytes.Length
            ];
            preambleHeader.CopyTo(payload, 0);
            nonce.CopyTo(payload, ChunkPreambleHeaderSize);
            encryptedBytes.CopyTo(payload, ChunkPreambleHeaderSize + EncryptionConstants.NonceSize);

            var manifestPath = Path.Combine(destinationRoot, BackupConstants.ManifestFileName);
            await WriteFileAtomicallyAsync(manifestPath, payload, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            errors.Add(new LocalizableMessage(MessageCode.ManifestWriteFailedFormat, ex.Message));
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

    private async Task WriteFileAtomicallyAsync(
        string finalPath,
        byte[] payload,
        CancellationToken cancellationToken
    )
    {
        var tempPath = finalPath + ".tmp";

        try
        {
            await fileOperationsService
                .WriteAllBytesAsync(tempPath, payload, cancellationToken)
                .ConfigureAwait(false);

            fileOperationsService.MoveFile(tempPath, finalPath, overwrite: true);
        }
        catch
        {
            try
            {
                fileOperationsService.DeleteFile(tempPath);
            }
            catch
            {
                // Ignore
            }

            throw;
        }
    }

    private static byte[] BuildChunkPreambleHeader(
        EncryptionAlgorithm algorithm,
        KeyDerivationAlgorithm keyDerivation,
        byte[] masterSalt
    )
    {
        if (masterSalt.Length != EncryptionConstants.SaltSize)
        {
            throw new ArgumentException(
                "Manifest master salt must be exactly 32 bytes.",
                nameof(masterSalt)
            );
        }

        var preambleHeader = new byte[ChunkPreambleHeaderSize];
        preambleHeader[0] = (byte)algorithm;
        preambleHeader[1] = (byte)keyDerivation;
        masterSalt.CopyTo(preambleHeader, 2);
        return preambleHeader;
    }

    private static bool TryDecodeBase64(string value, int expectedLength, out byte[] decoded)
    {
        decoded = Array.Empty<byte>();

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
            document.Compression
        );

        var files = document
            .Files.ConvertAll(static f => new ChunkManifestFileEntry(
                f.OriginalPath,
                f.FileHash,
                f.TotalSize,
                [.. f.Chunks]
            ))
;

        return new ChunkManifestData(header, document.MasterSalt, files);
    }

    private IEncryptionAlgorithmStrategy ResolveEncryptionStrategy(EncryptionAlgorithm algorithm)
    {
        return !encryptionStrategiesById.TryGetValue(algorithm, out var strategy)
            ? throw new ArgumentOutOfRangeException(
                nameof(algorithm),
                $"Encryption algorithm '{algorithm}' is not registered."
            )
            : strategy;
    }
}
