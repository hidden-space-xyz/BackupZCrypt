using System.Security.Cryptography;
using System.Text.Json;

using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;

namespace BackupZCrypt.Domain.Archive;

/// <summary>
/// Encodes and decodes the manifest file of an archive. The file is a 34-byte unencrypted preamble
/// header (cipher byte, key derivation byte, and 32-byte master salt) that also serves as the AEAD
/// associated data, followed by the 12-byte nonce and the AEAD-encrypted JSON document.
/// </summary>
/// <remarks>
/// This is the byte layout every archive already on disk was written with, so it lives in one
/// place, free of file-system access, and is pinned by the committed format fixtures.
/// </remarks>
public static class ManifestCodec
{
    /// <summary>
    /// The size in bytes of the unencrypted preamble header that opens a manifest: one encryption
    /// algorithm byte, one key derivation byte, and the 32-byte master salt.
    /// </summary>
    public const int PreambleHeaderSize = 34;

    /// <summary>
    /// The shortest manifest that can hold a preamble, a nonce, and an authentication tag.
    /// </summary>
    public const int MinimumManifestSize =
        PreambleHeaderSize + EncryptionConstants.NonceSize + EncryptionConstants.TagSize;

    /// <summary>
    /// The serializer settings of the manifest document. A member the document requires that is
    /// missing or <see langword="null"/> fails deserialization instead of reaching the backup engine
    /// as a default value.
    /// </summary>
    private static readonly JsonSerializerOptions DocumentSerializerOptions = new()
    {
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    /// <summary>
    /// Parses the unencrypted preamble of a manifest file, validating the algorithm identifiers.
    /// </summary>
    /// <param name="manifestFile">The complete manifest file.</param>
    /// <returns>The parsed preamble, or <see langword="null"/> when it names an algorithm this version does not have.</returns>
    /// <exception cref="InvalidDataException">The file is too short to hold a preamble and a nonce.</exception>
    public static ManifestPreamble? ReadPreamble(ReadOnlySpan<byte> manifestFile)
    {
        if (manifestFile.Length < PreambleHeaderSize + EncryptionConstants.NonceSize)
        {
            throw new InvalidDataException("Manifest is too short to hold a preamble.");
        }

        var algorithm = (EncryptionAlgorithm)manifestFile[0];
        var keyDerivation = (KeyDerivationAlgorithm)manifestFile[1];

        return !Enum.IsDefined(algorithm) || !Enum.IsDefined(keyDerivation)
            ? null
            : new ManifestPreamble(
                algorithm,
                keyDerivation,
                manifestFile.Slice(2, EncryptionConstants.SaltSize).ToArray(),
                manifestFile.Slice(PreambleHeaderSize, EncryptionConstants.NonceSize).ToArray(),
                manifestFile[(PreambleHeaderSize + EncryptionConstants.NonceSize)..].ToArray()
            );
    }

    /// <summary>
    /// Decrypts a manifest payload and combines the document with the algorithms and master salt of
    /// the preamble, which the ciphertext authenticates as associated data.
    /// </summary>
    /// <param name="preamble">The manifest preamble.</param>
    /// <param name="encryptionKey">The derived manifest encryption key.</param>
    /// <param name="encryption">The strategy implementing <see cref="ManifestPreamble.Algorithm"/>.</param>
    /// <returns>The decrypted manifest data, or <see langword="null"/> if decryption or validation failed.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static ChunkManifestData? Decrypt(
        ManifestPreamble preamble,
        byte[] encryptionKey,
        IEncryptionAlgorithmStrategy encryption
    )
    {
        ArgumentNullException.ThrowIfNull(preamble);
        ArgumentNullException.ThrowIfNull(encryptionKey);
        ArgumentNullException.ThrowIfNull(encryption);

        byte[]? plaintext = null;

        try
        {
            if (
                preamble.MasterSalt.Length != EncryptionConstants.SaltSize
                || preamble.Nonce.Length != EncryptionConstants.NonceSize
                || preamble.EncryptedPayload.Length is 0
            )
            {
                return null;
            }

            if (!Enum.IsDefined(preamble.Algorithm) || !Enum.IsDefined(preamble.KeyDerivation))
            {
                return null;
            }

            var associatedData = BuildPreambleHeader(
                preamble.Algorithm,
                preamble.KeyDerivation,
                preamble.MasterSalt
            );

            plaintext = encryption.DecryptChunk(
                preamble.EncryptedPayload,
                encryptionKey,
                preamble.Nonce,
                associatedData
            );

            var document = JsonSerializer.Deserialize<ChunkManifestDocument>(
                plaintext,
                DocumentSerializerOptions
            );

            return document is null || !Enum.IsDefined(document.Compression)
                ? null
                : ToChunkManifestData(preamble, document);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
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

    /// <summary>
    /// Serializes and encrypts a manifest, prefixing it with the preamble header and a freshly
    /// generated nonce.
    /// </summary>
    /// <remarks>
    /// The serialized document and its ciphertext are zeroed before returning; the returned file is
    /// owned by the caller, who should zero it once it has been written.
    /// </remarks>
    /// <param name="manifestData">The manifest contents to serialize and encrypt.</param>
    /// <param name="masterSalt">The decoded 32-byte master salt recorded in the preamble.</param>
    /// <param name="encryptionKey">The derived manifest encryption key.</param>
    /// <param name="algorithm">The encryption algorithm recorded in the preamble.</param>
    /// <param name="encryption">The strategy implementing <paramref name="algorithm"/>.</param>
    /// <returns>The complete manifest file.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="masterSalt"/> is not exactly 32 bytes long.</exception>
    public static byte[] Encrypt(
        ChunkManifestData manifestData,
        byte[] masterSalt,
        byte[] encryptionKey,
        EncryptionAlgorithm algorithm,
        IEncryptionAlgorithmStrategy encryption
    )
    {
        ArgumentNullException.ThrowIfNull(manifestData);
        ArgumentNullException.ThrowIfNull(masterSalt);
        ArgumentNullException.ThrowIfNull(encryptionKey);
        ArgumentNullException.ThrowIfNull(encryption);

        byte[]? manifestBytes = null;
        byte[]? encryptedBytes = null;

        try
        {
            ChunkManifestDocument document = new(
                manifestData.Header.Compression,
                [
                    .. manifestData
                        .Files.OrderBy(static f => f.OriginalPath, StringComparer.Ordinal)
                        .Select(ToSerializedEntry),
                ],
                manifestData.Directories is { Count: > 0 } directories
                    ? [.. directories.Order(StringComparer.Ordinal)]
                    : null
            );

            manifestBytes = JsonSerializer.SerializeToUtf8Bytes(document, DocumentSerializerOptions);

            var nonce = new byte[EncryptionConstants.NonceSize];
            RandomNumberGenerator.Fill(nonce);

            var preambleHeader = BuildPreambleHeader(
                algorithm,
                manifestData.Header.KeyDerivationAlgorithm,
                masterSalt
            );

            encryptedBytes = encryption.EncryptChunk(manifestBytes, encryptionKey, nonce, preambleHeader);

            var manifestFile = new byte[
                PreambleHeaderSize + EncryptionConstants.NonceSize + encryptedBytes.Length
            ];
            preambleHeader.CopyTo(manifestFile, 0);
            nonce.CopyTo(manifestFile, PreambleHeaderSize);
            encryptedBytes.CopyTo(manifestFile, PreambleHeaderSize + EncryptionConstants.NonceSize);

            return manifestFile;
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
    private static byte[] BuildPreambleHeader(
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

        var preambleHeader = new byte[PreambleHeaderSize];
        preambleHeader[0] = (byte)algorithm;
        preambleHeader[1] = (byte)keyDerivation;
        masterSalt.CopyTo(preambleHeader, 2);
        return preambleHeader;
    }

    /// <summary>
    /// Projects a deserialized manifest document into the in-memory manifest model used by the backup
    /// engine, taking the algorithms and the master salt from the preamble.
    /// </summary>
    /// <param name="preamble">The preamble the document was decrypted under.</param>
    /// <param name="document">The document deserialized from the decrypted manifest payload.</param>
    /// <returns>The equivalent manifest data.</returns>
    /// <exception cref="InvalidDataException">An entry's chunk list is missing or holds an empty reference.</exception>
    private static ChunkManifestData ToChunkManifestData(
        ManifestPreamble preamble,
        ChunkManifestDocument document
    )
    {
        return new ChunkManifestData(
            new ManifestHeader(preamble.Algorithm, preamble.KeyDerivation, document.Compression),
            Convert.ToBase64String(preamble.MasterSalt),
            document.Files.ConvertAll(static f => new ChunkManifestFileEntry(
                f.OriginalPath,
                f.FileHash,
                f.TotalSize,
                ExpandChunks(f),
                f.LastWriteTimeUtc,
                f.Attributes,
                f.UnixMode
            )),
            document.Directories is { } directories ? [.. directories] : null
        );
    }

    /// <summary>
    /// Returns the chunks of a serialized entry, rebuilding the single chunk of a file that leaves its
    /// chunk list out.
    /// </summary>
    /// <param name="entry">The serialized entry.</param>
    /// <returns>The entry's ordered chunk references.</returns>
    /// <exception cref="InvalidDataException">
    /// The chunk list holds an empty reference, or is missing although the file size needs one.
    /// </exception>
    private static List<ChunkManifestChunkRef> ExpandChunks(ChunkManifestFileEntrySerialized entry)
    {
        if (entry.Chunks is { } chunks)
        {
            return chunks.Any(static chunk => chunk is null)
                ? throw new InvalidDataException("Manifest chunk list holds an empty reference.")
                : [.. chunks];
        }

        return entry.TotalSize switch
        {
            0 => [],
            > 0 and <= BackupConstants.MaximumChunkSize =>
                [new ChunkManifestChunkRef(entry.FileHash, (int)entry.TotalSize)],
            _ => throw new InvalidDataException("Manifest entry omits the chunk list its size requires."),
        };
    }

    /// <summary>
    /// Projects an in-memory entry onto its on-disk shape, leaving the chunk list out when the entry
    /// itself implies it.
    /// </summary>
    /// <param name="entry">The entry to serialize.</param>
    /// <returns>The serialized entry.</returns>
    private static ChunkManifestFileEntrySerialized ToSerializedEntry(ChunkManifestFileEntry entry)
    {
        return new ChunkManifestFileEntrySerialized(
            entry.OriginalPath,
            entry.FileHash,
            entry.TotalSize,
            entry.LastWriteTimeUtc,
            ChunksAreImplied(entry) ? null : [.. entry.Chunks],
            entry.Attributes,
            entry.UnixMode
        );
    }

    /// <summary>
    /// Determines whether an entry's chunk list follows from the entry itself: an empty file has no
    /// chunks, and a file stored as one chunk is that chunk, with the file's hash and size.
    /// </summary>
    /// <param name="entry">The entry to inspect.</param>
    /// <returns><see langword="true"/> when the manifest can leave the chunk list out.</returns>
    private static bool ChunksAreImplied(ChunkManifestFileEntry entry)
    {
        return entry.Chunks switch
        {
            [] => entry.TotalSize is 0,
            [var chunk] => chunk.Size == entry.TotalSize
                && string.Equals(chunk.Hash, entry.FileHash, StringComparison.Ordinal),
            _ => false,
        };
    }
}
