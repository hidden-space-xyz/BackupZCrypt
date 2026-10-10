using System.Security.Cryptography;

using BackupZCrypt.Domain.Archive;
using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.ValueObjects.FileSystem;

namespace BackupZCrypt.Application.Engine;

/// <summary>
/// The checks and projections every operation applies to manifest entries: validating what a
/// manifest records before anything acts on it, decoding its hashes, and relating an entry to the
/// file it describes.
/// </summary>
internal static class ManifestEntries
{
    /// <summary>
    /// Validates every entry path, file hash, and chunk reference a manifest records, before any file
    /// is created, read, or overwritten.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This runs as one up-front sweep so a malformed or crafted manifest is rejected before a restore
    /// creates a directory, before a verify reads a chunk, and before an update rewrites the manifest.
    /// The decoded bytes serve only as a length check and are zeroed immediately.
    /// </para>
    /// <para>
    /// Paths are recorded in a single canonical form, so two entries are duplicates when their paths
    /// are identical. Paths that only differ in letter case are distinct files on the system that wrote
    /// them; a restore onto a system that ignores case reports the clash for the second file instead of
    /// rejecting the whole archive.
    /// </para>
    /// </remarks>
    /// <param name="manifestFiles">The manifest entries to validate.</param>
    /// <exception cref="InvalidDataException">
    /// An entry path is invalid or duplicated, a size is impossible, one chunk hash is recorded with two
    /// sizes, or chunk sizes do not add up to the file size.
    /// </exception>
    /// <exception cref="CryptographicException">
    /// A file hash or chunk hash is not canonical Base64 of the expected length.
    /// </exception>
    internal static void Validate(
        IReadOnlyList<ChunkManifestFileEntry> manifestFiles
    )
    {
        HashSet<string> paths = new(StringComparer.Ordinal);
        Dictionary<string, int> chunkSizes = new(StringComparer.Ordinal);

        foreach (var file in manifestFiles)
        {
            ManifestPathPolicy.ValidateRelative(file.OriginalPath);
            if (!paths.Add(file.OriginalPath))
            {
                throw new InvalidDataException("Manifest contains duplicate file paths.");
            }

            if (file.TotalSize < 0)
            {
                throw new InvalidDataException("Manifest file size cannot be negative.");
            }

            EnsureCanonicalHash(file.FileHash, "Invalid file hash.");

            long totalChunkSize = 0;

            foreach (var chunk in file.Chunks)
            {
                if (chunk.Size is <= 0 or > BackupConstants.MaximumChunkSize)
                {
                    throw new InvalidDataException("Manifest chunk size is outside the supported range.");
                }

                try
                {
                    totalChunkSize = checked(totalChunkSize + chunk.Size);
                }
                catch (OverflowException ex)
                {
                    throw new InvalidDataException("Manifest chunk sizes overflow the file size.", ex);
                }

                EnsureCanonicalHash(chunk.Hash, "Invalid chunk hash.");

                if (!chunkSizes.TryAdd(chunk.Hash, chunk.Size) && chunkSizes[chunk.Hash] != chunk.Size)
                {
                    throw new InvalidDataException("One chunk hash has inconsistent size metadata.");
                }
            }

            if (totalChunkSize != file.TotalSize)
            {
                throw new InvalidDataException(
                    "Manifest chunk sizes do not add up to the declared file size."
                );
            }
        }
    }

    /// <summary>
    /// Confirms that a hash read from the manifest is the canonical Base64 form of a SHA-256 digest.
    /// </summary>
    /// <param name="hash">The Base64 hash to check.</param>
    /// <param name="errorMessage">The message carried by the exception when the hash is invalid.</param>
    /// <exception cref="CryptographicException">The hash is not canonical Base64 of a SHA-256 digest.</exception>
    private static void EnsureCanonicalHash(string hash, string errorMessage)
    {
        var decoded = DecodeBase64FixedLength(hash, SHA256.HashSizeInBytes, errorMessage);

        try
        {
            if (!string.Equals(hash, Convert.ToBase64String(decoded), StringComparison.Ordinal))
            {
                throw new CryptographicException(errorMessage);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decoded);
        }
    }

    /// <summary>
    /// Decodes a Base64 value read from the manifest and enforces its exact decoded length.
    /// </summary>
    /// <remarks>
    /// Length is checked before the bytes reach any cryptographic primitive, and a buffer of the
    /// wrong size is zeroed before the failure is raised.
    /// </remarks>
    /// <param name="value">The Base64 text to decode.</param>
    /// <param name="expectedLength">The exact number of bytes the value must decode to.</param>
    /// <param name="errorMessage">The message carried by the exception when validation fails.</param>
    /// <returns>The decoded bytes.</returns>
    /// <exception cref="CryptographicException">The value is not valid Base64 or decodes to the wrong length.</exception>
    internal static byte[] DecodeBase64FixedLength(
        string value,
        int expectedLength,
        string errorMessage
    )
    {
        byte[] decoded;

        try
        {
            decoded = Convert.FromBase64String(value);
        }
        catch (FormatException ex)
        {
            throw new CryptographicException(errorMessage, ex);
        }

        if (decoded.Length != expectedLength)
        {
            CryptographicOperations.ZeroMemory(decoded);
            throw new CryptographicException(errorMessage);
        }

        return decoded;
    }

    /// <summary>
    /// Determines whether a recorded entry still describes the file on disk, judged by its size and
    /// modification time, the way most backup tools detect changes without reading the file.
    /// </summary>
    /// <param name="entry">The recorded manifest entry.</param>
    /// <param name="metadata">The file's current metadata.</param>
    /// <returns><see langword="true"/> when the file can be carried over without being read.</returns>
    internal static bool IsUnchanged(ChunkManifestFileEntry entry, FileMetadata metadata)
    {
        return entry.TotalSize == metadata.Size
            && entry.LastWriteTimeUtc.ToUniversalTime().Ticks
                == metadata.LastWriteTimeUtc.ToUniversalTime().Ticks;
    }

    /// <summary>
    /// Attaches the metadata read from the source file to the entry produced for it.
    /// </summary>
    /// <param name="entry">The entry produced by chunking the file.</param>
    /// <param name="metadata">The file's metadata, read before its content.</param>
    /// <returns>The entry carrying the file's modification time and attributes.</returns>
    internal static ChunkManifestFileEntry WithMetadata(ChunkManifestFileEntry entry, FileMetadata metadata)
    {
        var attributes =
            (metadata.IsReadOnly ? ManifestFileAttributes.ReadOnly : ManifestFileAttributes.None)
            | (metadata.IsHidden ? ManifestFileAttributes.Hidden : ManifestFileAttributes.None);

        return entry with
        {
            LastWriteTimeUtc = DateTime.SpecifyKind(metadata.LastWriteTimeUtc, DateTimeKind.Utc),
            Attributes = attributes is ManifestFileAttributes.None ? null : attributes,
            UnixMode = metadata.UnixMode,
        };
    }
}
