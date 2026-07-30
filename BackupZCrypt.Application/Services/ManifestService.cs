using System.Security.Cryptography;
using System.Text.Json;

using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects.Manifest;
using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Factories.Interfaces;
using BackupZCrypt.Domain.Services.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Application.Services;

/// <summary>
/// Reads, writes, decrypts, and classifies backup manifests. Chunked manifests are stored as a
/// 34-byte unencrypted preamble header (algorithm byte, key derivation byte, and 32-byte master
/// salt) that also serves as the AEAD associated data, followed by the 12-byte nonce and the
/// AEAD-encrypted document, and are written atomically via a temp file and rename.
/// </summary>
/// <param name="fileOperationsService">The service used to read and write manifest files.</param>
/// <param name="encryptionServiceFactory">The factory producing encryption strategies for an algorithm.</param>
internal sealed class ManifestService(
    IFileOperationsService fileOperationsService,
    IEncryptionServiceFactory encryptionServiceFactory
) : IManifestService
{
    /// <summary>
    /// The size in bytes of the unencrypted preamble header that opens a chunked manifest: one encryption
    /// algorithm byte, one key derivation byte, and the 32-byte master salt.
    /// </summary>
    private const int ChunkPreambleHeaderSize = 34;

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
    /// <exception cref="ArgumentNullException">
    /// <paramref name="preamble"/> or <paramref name="encryptionKey"/> is <see langword="null"/>.
    /// </exception>
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

            var encryptionStrategy = encryptionServiceFactory.Create(preamble.Algorithm);
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
    /// <exception cref="ArgumentNullException">
    /// <paramref name="manifestData"/> or <paramref name="encryptionKey"/> is <see langword="null"/>.
    /// </exception>
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

            var encryptionStrategy = encryptionServiceFactory.Create(algorithm);
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

    /// <summary>
    /// Writes the payload to a sibling temp file and renames it over the target, so an interrupted write can
    /// never leave a truncated manifest in place of a valid one.
    /// </summary>
    /// <remarks>
    /// If the write or the rename fails, the temp file is deleted on a best-effort basis and any cleanup
    /// failure is swallowed, because the original write failure is rethrown and must not be masked.
    /// </remarks>
    /// <param name="finalPath">The path the manifest must end up at.</param>
    /// <param name="payload">The bytes to write.</param>
    /// <param name="cancellationToken">A token to cancel the write.</param>
    /// <returns>A task that completes once the file has been renamed into place.</returns>
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
            }

            throw;
        }
    }

    /// <summary>
    /// Builds the fixed-size preamble header that is stored unencrypted at the head of the manifest and also
    /// serves as the AEAD associated data, binding the ciphertext to the algorithms and master salt.
    /// </summary>
    /// <param name="algorithm">The encryption algorithm the manifest is protected with.</param>
    /// <param name="keyDerivation">The key derivation algorithm used to derive the master key.</param>
    /// <param name="masterSalt">The 32-byte master salt.</param>
    /// <returns>The assembled preamble header.</returns>
    /// <exception cref="ArgumentException"><paramref name="masterSalt"/> is not exactly 32 bytes long.</exception>
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

    /// <summary>
    /// Attempts to decode a Base64 string and confirm that it yields exactly the expected number of bytes.
    /// </summary>
    /// <param name="value">The Base64 text to decode.</param>
    /// <param name="expectedLength">The byte length the decoded value must have.</param>
    /// <param name="decoded">
    /// Receives the decoded bytes; empty when <paramref name="value"/> is missing or not valid Base64, but
    /// populated with the decoded bytes when only the length check failed.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the value decoded to exactly <paramref name="expectedLength"/> bytes;
    /// otherwise <see langword="false"/>.
    /// </returns>
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

    /// <summary>
    /// Projects a deserialized manifest document into the in-memory manifest model used by the backup engine.
    /// </summary>
    /// <param name="document">The document deserialized from the decrypted manifest payload.</param>
    /// <returns>The equivalent manifest data.</returns>
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
}
