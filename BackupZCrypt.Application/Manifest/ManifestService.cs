using System.Security.Cryptography;

using BackupZCrypt.Application.Common;
using BackupZCrypt.Application.FileSystem;
using BackupZCrypt.Domain.Archive;
using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;

namespace BackupZCrypt.Application.Manifest;

/// <summary>
/// Reads, writes, decrypts, and classifies the manifests of backup folders. The byte layout of the
/// manifest is <see cref="ManifestCodec"/>'s; this service adds the file access, writing the file
/// atomically via a temporary file and rename, and turns failures into results the caller reports.
/// </summary>
/// <param name="fileOperationsService">The service used to read and write manifest files.</param>
/// <param name="encryptionStrategies">The registered encryption strategies, looked up by algorithm.</param>
internal sealed class ManifestService(
    IFileOperationsService fileOperationsService,
    IStrategyRegistry<EncryptionAlgorithm, IEncryptionAlgorithmStrategy> encryptionStrategies
) : IManifestService
{
    /// <summary>
    /// Classifies the location a user picked as a backup: a missing path, a file instead of the backup
    /// folder, a folder without a manifest, or a folder whose manifest is readable, damaged, or names an
    /// algorithm this version does not support.
    /// </summary>
    /// <param name="backupPath">The path the user picked as the backup folder.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The detected manifest kind.</returns>
    /// <exception cref="ArgumentException"><paramref name="backupPath"/> is <see langword="null"/> or whitespace.</exception>
    public async Task<ManifestKind> DetectManifestKindAsync(
        string backupPath,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);

        try
        {
            if (!fileOperationsService.DirectoryExists(backupPath))
            {
                return fileOperationsService.FileExists(backupPath)
                    ? ManifestKind.NotADirectory
                    : ManifestKind.PathNotFound;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return ManifestKind.PathNotFound;
        }

        return await this.InspectManifestAsync(backupPath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inspects the manifest in a backup folder without decrypting it: its presence, its length, and
    /// the algorithm identifiers of its preamble.
    /// </summary>
    /// <param name="backupRoot">The backup folder.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// <see cref="ManifestKind.Encrypted"/> for a manifest that can be opened with a password, or the
    /// kind describing why it cannot.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="backupRoot"/> is <see langword="null"/> or whitespace.</exception>
    public async Task<ManifestKind> InspectManifestAsync(
        string backupRoot,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupRoot);

        try
        {
            var manifestPath = Path.Join(
                backupRoot,
                BackupConstants.ManifestFileName
            );

            if (!fileOperationsService.FileExists(manifestPath))
            {
                return ManifestKind.Missing;
            }

            await using var stream = fileOperationsService.OpenReadStream(
                manifestPath,
                bufferSize: 16
            );

            if (stream.Length < ManifestCodec.MinimumManifestSize)
            {
                return ManifestKind.Damaged;
            }

            var identifiers = new byte[2];
            await stream.ReadExactlyAsync(identifiers, cancellationToken).ConfigureAwait(false);

            return
                Enum.IsDefined((EncryptionAlgorithm)identifiers[0])
                && Enum.IsDefined((KeyDerivationAlgorithm)identifiers[1])
                ? ManifestKind.Encrypted
                : ManifestKind.Unsupported;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return ManifestKind.Damaged;
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

        byte[]? rawFile = null;

        try
        {
            var manifestPath = Path.Join(sourceRoot, BackupConstants.ManifestFileName);
            if (!fileOperationsService.FileExists(manifestPath))
            {
                return null;
            }

            rawFile = await fileOperationsService
                .ReadAllBytesBoundedAsync(manifestPath, Array.MaxLength, cancellationToken)
                .ConfigureAwait(false);

            return ManifestCodec.ReadPreamble(rawFile);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
        finally
        {
            if (rawFile is not null)
            {
                CryptographicOperations.ZeroMemory(rawFile);
            }
        }
    }

    /// <summary>
    /// Decrypts a chunked manifest payload with the strategy its preamble names.
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

        IEncryptionAlgorithmStrategy encryption;

        try
        {
            encryption = encryptionStrategies.Resolve(preamble.Algorithm);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }

        return ManifestCodec.Decrypt(preamble, encryptionKey, encryption);
    }

    /// <summary>
    /// Serializes, encrypts, and atomically writes a chunked manifest.
    /// </summary>
    /// <remarks>
    /// The master salt is decoded with <see cref="Convert.TryFromBase64String"/> rather than parsed,
    /// so a malformed value becomes a reported rejection carrying its own message code. Parsing would
    /// raise a <see cref="FormatException"/> that the backup operations could only surface as an
    /// untranslated .NET string spliced into a localized sentence.
    /// </remarks>
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
        byte[]? manifestFile = null;

        try
        {
            var masterSalt = new byte[EncryptionConstants.SaltSize];
            if (
                !Convert.TryFromBase64String(manifestData.MasterSalt, masterSalt, out var saltLength)
                || saltLength != EncryptionConstants.SaltSize
            )
            {
                errors.Add(new LocalizableMessage(MessageCode.ManifestInvalidMasterSalt));
                return errors;
            }

            if (
                !Enum.IsDefined(algorithm)
                || !Enum.IsDefined(manifestData.Header.KeyDerivationAlgorithm)
                || !Enum.IsDefined(manifestData.Header.Compression)
            )
            {
                errors.Add(new LocalizableMessage(MessageCode.ManifestUnsupportedAlgorithm));
                return errors;
            }

            manifestFile = ManifestCodec.Encrypt(
                manifestData,
                masterSalt,
                encryptionKey,
                algorithm,
                encryptionStrategies.Resolve(algorithm)
            );

            var manifestPath = Path.Join(destinationRoot, BackupConstants.ManifestFileName);
            await fileOperationsService
                .WriteAllBytesAtomicallyAsync(manifestPath, manifestFile, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            errors.Add(new LocalizableMessage(MessageCode.ManifestWriteFailedFormat, ex.Message));
        }
        finally
        {
            if (manifestFile is not null)
            {
                CryptographicOperations.ZeroMemory(manifestFile);
            }
        }

        return errors;
    }
}
