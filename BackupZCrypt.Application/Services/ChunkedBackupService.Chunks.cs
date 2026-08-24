using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

using BackupZCrypt.Application.Utilities.Extensions;
using BackupZCrypt.Application.Utilities.Helpers;
using BackupZCrypt.Application.ValueObjects.Manifest;
using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;

namespace BackupZCrypt.Application.Services;

internal sealed partial class ChunkedBackupService
{
    /// <summary>
    /// Splits a file into content-defined chunks, stores each chunk encrypted on disk, hashes the
    /// whole file, and returns the manifest entry that reconstructs it.
    /// </summary>
    /// <remarks>
    /// Chunks are deduplicated through <paramref name="storedChunks"/>, so a chunk already being
    /// stored for another file is awaited instead of being encrypted and written a second time.
    /// </remarks>
    /// <param name="filePath">The absolute path of the file to read.</param>
    /// <param name="relativePath">The file's path relative to the backup root, as recorded in the manifest.</param>
    /// <param name="chunksDir">The directory encrypted chunk files are written into.</param>
    /// <param name="cipher">The key material and strategies chunks are compressed and encrypted with.</param>
    /// <param name="storedChunks">The shared cache mapping a chunk hash to its in-flight or completed store operation.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The manifest entry describing the file and its ordered chunk references.</returns>
    private async Task<ChunkManifestFileEntry> ChunkAndEncryptFileAsync(
        string filePath,
        string relativePath,
        string chunksDir,
        ChunkCipherSet cipher,
        ConcurrentDictionary<string, Lazy<Task<string>>> storedChunks,
        CancellationToken cancellationToken
    )
    {
        ManifestPathPolicy.ValidateRelative(relativePath);
        List<ChunkManifestChunkRef> chunkRefs = [];

        await using var fileStream = fileOperationsService.OpenReadStream(
            filePath,
            StreamConstants.CopyBufferSize
        );

        using var fileHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        long actualFileSize = 0;
        await foreach (
            var chunkData in chunkingStrategy
                .ChunkAsync(fileStream, cancellationToken)
                .ConfigureAwait(false)
        )
        {
            byte[]? chunkHash = null;

            try
            {
                actualFileSize = checked(actualFileSize + chunkData.Length);
                fileHasher.AppendData(chunkData.Span);

                chunkHash = SHA256.HashData(chunkData.Span);
                var chunkHashB64 = Convert.ToBase64String(chunkHash);
                var chunkFilePath = this.ComputeChunkFilePath(
                    chunksDir,
                    cipher.NamingKey,
                    chunkHash
                );

                var chunkOperation = new Lazy<Task<string>>(
                    () =>
                        EncryptAndStoreChunkAsync(
                            chunkData,
                            chunkHash,
                            chunkFilePath,
                            cipher.ChunkEncryptionKey,
                            cipher.ChunkNonceKey,
                            cipher.EncryptionStrategy,
                            cipher.CompressionStrategy,
                            cancellationToken
                        ),
                    LazyThreadSafetyMode.ExecutionAndPublication
                );

                var storedChunk = storedChunks.GetOrAdd(chunkHashB64, chunkOperation);

                var nonceB64 = await AwaitStoredChunkNonceAsync(
                        chunkHashB64,
                        storedChunk,
                        chunkOperation,
                        storedChunks
                    )
                    .ConfigureAwait(false);

                chunkRefs.Add(new ChunkManifestChunkRef(chunkHashB64, chunkData.Length, nonceB64));
            }
            finally
            {
                if (chunkHash is not null)
                {
                    CryptographicOperations.ZeroMemory(chunkHash);
                }

                if (MemoryMarshal.TryGetArray(chunkData, out var chunkSegment))
                {
                    CryptographicOperations.ZeroMemory(
                        chunkSegment.Array!.AsSpan(chunkSegment.Offset, chunkSegment.Count)
                    );
                }
            }
        }

        var fileHash = fileHasher.GetHashAndReset();

        try
        {
            return new ChunkManifestFileEntry(
                relativePath,
                Convert.ToBase64String(fileHash),
                actualFileSize,
                chunkRefs
            );
        }
        finally
        {
            CryptographicOperations.ZeroMemory(fileHash);
        }
    }

    /// <summary>
    /// Compresses a chunk when compression is enabled, encrypts it, and writes the ciphertext to its
    /// content-addressed file.
    /// </summary>
    /// <remarks>
    /// Compression runs before encryption so ciphertext is never compressed. The nonce is derived
    /// deterministically from the chunk hash, which lets identical content deduplicate while keeping
    /// the nonce key-dependent, and the chunk hash concatenated with that nonce is bound in as
    /// associated data. The compressed copy of the chunk, the ciphertext, the associated data, and
    /// the nonce are zeroed before returning; the plaintext buffer is owned by the caller.
    /// </remarks>
    /// <param name="chunkData">The plaintext bytes of the chunk.</param>
    /// <param name="chunkHash">The SHA-256 content hash of the chunk.</param>
    /// <param name="chunkFilePath">The full path the encrypted chunk is written to.</param>
    /// <param name="encryptionKey">The chunk encryption sub-key.</param>
    /// <param name="nonceKey">The sub-key the chunk nonce is derived from.</param>
    /// <param name="encryptionStrategy">The strategy used to encrypt the chunk.</param>
    /// <param name="compressionStrategy">The compression strategy, or <see langword="null"/> to store the chunk uncompressed.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The Base64-encoded nonce recorded for the chunk in the manifest.</returns>
    private async Task<string> EncryptAndStoreChunkAsync(
        ReadOnlyMemory<byte> chunkData,
        byte[] chunkHash,
        string chunkFilePath,
        byte[] encryptionKey,
        byte[] nonceKey,
        IEncryptionAlgorithmStrategy encryptionStrategy,
        ICompressionStrategy? compressionStrategy,
        CancellationToken cancellationToken
    )
    {
        var nonce = ChunkCryptoHelper.ComputeChunkNonce(nonceKey, chunkHash);
        byte[]? dataToEncrypt = null;
        byte[]? encrypted = null;
        byte[]? associatedData = null;

        try
        {
            var nonceB64 = Convert.ToBase64String(nonce);

            if (compressionStrategy is not null)
            {
                await using var inputStream = CreateReadOnlyStream(chunkData);

                await using var compressedStream = await compressionStrategy
                    .CompressAsync(inputStream, cancellationToken)
                    .ConfigureAwait(false);

                if (compressedStream is MemoryStream compressedMemory)
                {
                    dataToEncrypt = compressedMemory.ToArray();
                    if (compressedMemory.TryGetBuffer(out var compressedSegment))
                    {
                        CryptographicOperations.ZeroMemory(
                            compressedSegment.Array!.AsSpan(
                                compressedSegment.Offset,
                                compressedSegment.Count
                            )
                        );
                    }
                }
                else
                {
                    await using MemoryStream compressedBuffer = new();
                    await compressedStream
                        .CopyToAsync(compressedBuffer, cancellationToken)
                        .ConfigureAwait(false);

                    dataToEncrypt = compressedBuffer.ToArray();
                    if (compressedBuffer.TryGetBuffer(out var compressedSegment))
                    {
                        CryptographicOperations.ZeroMemory(
                            compressedSegment.Array!.AsSpan(
                                compressedSegment.Offset,
                                compressedSegment.Count
                            )
                        );
                    }
                }
            }

            if (
                dataToEncrypt is not null
                && dataToEncrypt.Length > MaximumStoredChunkSize - EncryptionConstants.TagSize
            )
            {
                throw new InvalidDataException("Compressed chunk exceeds the supported size.");
            }

            associatedData = ChunkCryptoHelper.BuildChunkAssociatedData(chunkHash, nonce);
            encrypted = dataToEncrypt is not null
                ? encryptionStrategy.EncryptChunk(
                    dataToEncrypt,
                    encryptionKey,
                    nonce,
                    associatedData
                )
                : encryptionStrategy.EncryptChunk(
                    chunkData.Span,
                    encryptionKey,
                    nonce,
                    associatedData
                );

            await fileOperationsService
                .WriteAllBytesAtomicallyAsync(chunkFilePath, encrypted, cancellationToken)
                .ConfigureAwait(false);

            return nonceB64;
        }
        finally
        {
            if (dataToEncrypt is not null)
            {
                CryptographicOperations.ZeroMemory(dataToEncrypt);
            }

            if (encrypted is not null)
            {
                CryptographicOperations.ZeroMemory(encrypted);
            }

            if (associatedData is not null)
            {
                CryptographicOperations.ZeroMemory(associatedData);
            }

            CryptographicOperations.ZeroMemory(nonce);
        }
    }

    /// <summary>
    /// Builds the cache that maps each chunk hash referenced by a manifest to the nonce its stored
    /// ciphertext authenticates under.
    /// </summary>
    /// <remarks>
    /// A chunk's nonce is a deterministic function of the chunk-nonce sub-key and the chunk's content
    /// hash, and that sub-key is fixed for an archive's whole lifetime because the master salt is
    /// preserved across updates. One hash can therefore carry exactly one nonce; a manifest that
    /// records two for the same hash is rejected rather than guessed at. The entries are shared with
    /// the write path, which resolves its own asynchronously as chunks are stored.
    /// </remarks>
    /// <param name="manifestFiles">The manifest entries whose chunk references are indexed.</param>
    /// <returns>A cache keyed by Base64 chunk hash whose values resolve to the recorded Base64 nonce.</returns>
    /// <exception cref="CryptographicException">A chunk hash carries more than one distinct nonce.</exception>
    private static ConcurrentDictionary<string, Lazy<Task<string>>> BuildStoredChunkNonceCache(
        IReadOnlyList<ChunkManifestFileEntry> manifestFiles
    )
    {
        var chunkNonceCandidates = BuildChunkNonceCandidates(manifestFiles);
        ConcurrentDictionary<string, Lazy<Task<string>>> storedChunks = new(StringComparer.Ordinal);

        foreach (var (chunkHashB64, nonceCandidates) in chunkNonceCandidates)
        {
            if (nonceCandidates.Length is not 1)
            {
                throw new CryptographicException(
                    "A chunk hash carries more than one distinct nonce."
                );
            }

            var nonceB64 = nonceCandidates[0];
            var storedChunk = new Lazy<Task<string>>(
                () => Task.FromResult(nonceB64),
                LazyThreadSafetyMode.ExecutionAndPublication
            );

            _ = storedChunks.TryAdd(chunkHashB64, storedChunk);
        }

        return storedChunks;
    }

    /// <summary>
    /// Removes preloaded deduplication entries whose stored chunk is absent, linked, or has an
    /// impossible ciphertext size, so an update regenerates them from the source.
    /// </summary>
    /// <param name="storedChunks">The preloaded hash-to-nonce cache to filter.</param>
    /// <param name="manifestFiles">The existing manifest entries that describe stored chunks.</param>
    /// <param name="chunksDir">The archive directory that should contain the chunk files.</param>
    /// <param name="namingKey">The sub-key used to derive chunk file names.</param>
    /// <param name="compressionStrategy">The archive compression strategy, or <see langword="null"/>.</param>
    private void RemoveUnavailableStoredChunks(
        ConcurrentDictionary<string, Lazy<Task<string>>> storedChunks,
        IReadOnlyList<ChunkManifestFileEntry> manifestFiles,
        string chunksDir,
        byte[] namingKey,
        ICompressionStrategy? compressionStrategy
    )
    {
        HashSet<string> inspectedHashes = new(StringComparer.Ordinal);

        foreach (var chunk in manifestFiles.SelectMany(static file => file.Chunks))
        {
            if (
                inspectedHashes.Add(chunk.Hash)
                && !this.IsStoredChunkAvailable(
                    chunk,
                    chunksDir,
                    namingKey,
                    compressionStrategy
                )
            )
            {
                _ = storedChunks.TryRemove(chunk.Hash, out _);
            }
        }
    }

    /// <summary>
    /// Checks whether one stored chunk can safely be reused without reading it into memory.
    /// </summary>
    /// <param name="chunk">The manifest reference describing the plaintext chunk.</param>
    /// <param name="chunksDir">The directory expected to contain its ciphertext.</param>
    /// <param name="namingKey">The sub-key used to derive its on-disk name.</param>
    /// <param name="compressionStrategy">The archive compression strategy, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the entry is a regular file of a plausible size.</returns>
    private bool IsStoredChunkAvailable(
        ChunkManifestChunkRef chunk,
        string chunksDir,
        byte[] namingKey,
        ICompressionStrategy? compressionStrategy
    )
    {
        byte[]? chunkHash = null;

        try
        {
            chunkHash = DecodeBase64FixedLength(
                chunk.Hash,
                SHA256.HashSizeInBytes,
                "Invalid chunk hash."
            );
            var chunkFilePath = this.ComputeChunkFilePath(chunksDir, namingKey, chunkHash);

            if (
                !fileOperationsService.FileExists(chunkFilePath)
                || fileOperationsService.IsReparsePoint(chunkFilePath)
            )
            {
                return false;
            }

            var storedSize = fileOperationsService.GetFileSize(chunkFilePath);
            var maximumStoredSize = compressionStrategy is null
                ? checked(chunk.Size + EncryptionConstants.TagSize)
                : MaximumStoredChunkSize;

            return storedSize >= EncryptionConstants.TagSize
                && storedSize <= maximumStoredSize
                && (compressionStrategy is not null || storedSize == maximumStoredSize);
        }
        catch (Exception exception) when (IsFileLevelError(exception))
        {
            return false;
        }
        finally
        {
            if (chunkHash is not null)
            {
                CryptographicOperations.ZeroMemory(chunkHash);
            }
        }
    }

    /// <summary>
    /// Validates every entry path, chunk hash, and chunk nonce a manifest records, before any file is
    /// created, read, or overwritten.
    /// </summary>
    /// <remarks>
    /// This runs as one up-front sweep so a malformed or crafted manifest is rejected before a restore
    /// creates a directory, before a verify reads a chunk, and before an update rewrites the manifest.
    /// The decoded bytes serve only as a length check and are zeroed immediately.
    /// </remarks>
    /// <param name="manifestFiles">The manifest entries to validate.</param>
    /// <exception cref="InvalidDataException">
    /// An entry path is invalid or duplicated, a size is impossible, or chunk sizes do not add up
    /// to the file size.
    /// </exception>
    /// <exception cref="CryptographicException">
    /// A file hash, chunk hash, or nonce is not canonical Base64 of the expected length.
    /// </exception>
    private static void ValidateManifestEntries(
        IReadOnlyList<ChunkManifestFileEntry> manifestFiles
    )
    {
        HashSet<string> paths = new(StringComparer.FromComparison(PathComparer));

        Dictionary<string, (int Size, string Nonce)> chunkMetadata = new(StringComparer.Ordinal);

        foreach (var file in manifestFiles)
        {
            ManifestPathPolicy.ValidateRelative(file.OriginalPath);
            var canonicalPath = ManifestPathPolicy.Canonicalize(file.OriginalPath);
            if (!paths.Add(canonicalPath))
            {
                throw new InvalidDataException("Manifest contains duplicate file paths.");
            }

            if (file.TotalSize < 0)
            {
                throw new InvalidDataException("Manifest file size cannot be negative.");
            }

            var decodedFileHash = DecodeBase64FixedLength(
                file.FileHash,
                SHA256.HashSizeInBytes,
                "Invalid file hash."
            );

            try
            {
                if (
                    !string.Equals(
                        file.FileHash,
                        Convert.ToBase64String(decodedFileHash),
                        StringComparison.Ordinal
                    )
                )
                {
                    throw new CryptographicException("File hash is not canonical Base64.");
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(decodedFileHash);
            }

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

                byte[]? decodedHash = null;
                byte[]? decodedNonce = null;

                try
                {
                    decodedHash = DecodeBase64FixedLength(
                        chunk.Hash,
                        SHA256.HashSizeInBytes,
                        "Invalid chunk hash."
                    );
                    decodedNonce = DecodeBase64FixedLength(
                        chunk.Nonce,
                        EncryptionConstants.NonceSize,
                        "Invalid chunk nonce."
                    );

                    var canonicalHash = Convert.ToBase64String(decodedHash);
                    var canonicalNonce = Convert.ToBase64String(decodedNonce);

                    if (
                        !string.Equals(chunk.Hash, canonicalHash, StringComparison.Ordinal)
                        || !string.Equals(chunk.Nonce, canonicalNonce, StringComparison.Ordinal)
                    )
                    {
                        throw new CryptographicException(
                            "Chunk hash or nonce is not canonical Base64."
                        );
                    }

                    if (chunkMetadata.TryGetValue(canonicalHash, out var metadata))
                    {
                        if (
                            metadata.Size != chunk.Size
                            || !string.Equals(
                                metadata.Nonce,
                                canonicalNonce,
                                StringComparison.Ordinal
                            )
                        )
                        {
                            throw new InvalidDataException(
                                "One chunk hash has inconsistent size or nonce metadata."
                            );
                        }
                    }
                    else
                    {
                        chunkMetadata.Add(canonicalHash, (chunk.Size, canonicalNonce));
                    }
                }
                finally
                {
                    if (decodedHash is not null)
                    {
                        CryptographicOperations.ZeroMemory(decodedHash);
                    }

                    if (decodedNonce is not null)
                    {
                        CryptographicOperations.ZeroMemory(decodedNonce);
                    }
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
    /// Groups the distinct nonces a manifest records for each chunk hash.
    /// </summary>
    /// <remarks>
    /// The entries are expected to have passed <see cref="ValidateManifestEntries"/> already; the
    /// Base64 forms are what the caller keys and returns.
    /// </remarks>
    /// <param name="manifestFiles">The manifest entries to scan.</param>
    /// <returns>A dictionary mapping each Base64 chunk hash to its distinct Base64 nonces.</returns>
    private static Dictionary<string, string[]> BuildChunkNonceCandidates(
        IReadOnlyList<ChunkManifestFileEntry> manifestFiles
    )
    {
        Dictionary<string, List<string>> chunkNonceCandidates = new(StringComparer.Ordinal);

        foreach (var file in manifestFiles)
        {
            foreach (var chunk in file.Chunks)
            {
                if (!chunkNonceCandidates.TryGetValue(chunk.Hash, out var nonceCandidates))
                {
                    nonceCandidates = [];
                    chunkNonceCandidates.Add(chunk.Hash, nonceCandidates);
                }

                if (!nonceCandidates.Contains(chunk.Nonce, StringComparer.Ordinal))
                {
                    nonceCandidates.Add(chunk.Nonce);
                }
            }
        }

        return chunkNonceCandidates.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.ToArray(),
            StringComparer.Ordinal
        );
    }

    /// <summary>
    /// Awaits the store operation for a chunk and returns its nonce, evicting a failed operation from
    /// the cache so a later file re-attempts it instead of reusing a permanently faulted task.
    /// </summary>
    /// <remarks>
    /// Only the operation this caller published is evicted, identified by reference, so an entry a
    /// concurrent caller won the race with is never removed. The failure is always rethrown.
    /// </remarks>
    /// <param name="chunkHashB64">The Base64-encoded content hash keying the chunk in the cache.</param>
    /// <param name="storedChunk">The operation actually held in the cache, which may belong to another caller.</param>
    /// <param name="candidateChunk">The operation this caller offered when adding the entry.</param>
    /// <param name="storedChunks">The shared cache of chunk store operations.</param>
    /// <returns>The Base64-encoded nonce the chunk was stored with.</returns>
    private static async Task<string> AwaitStoredChunkNonceAsync(
        string chunkHashB64,
        Lazy<Task<string>> storedChunk,
        Lazy<Task<string>> candidateChunk,
        ConcurrentDictionary<string, Lazy<Task<string>>> storedChunks
    )
    {
        try
        {
            return await storedChunk.Value.ConfigureAwait(false);
        }
        catch
        {
            if (ReferenceEquals(storedChunk, candidateChunk))
            {
                _ = storedChunks.TryRemove(
                    new KeyValuePair<string, Lazy<Task<string>>>(chunkHashB64, candidateChunk)
                );
            }

            throw;
        }
    }

    /// <summary>
    /// Orders manifest entries by path and rewrites every chunk reference with the nonce the stored
    /// chunk actually authenticates under.
    /// </summary>
    /// <remarks>
    /// An update mixes entries carried over from the previous manifest with entries produced by this
    /// run, so every chunk reference is re-emitted from the shared resolution cache and the entries are
    /// ordered by path, keeping the written manifest identical for identical content.
    /// </remarks>
    /// <param name="entries">The entries collected for the new manifest.</param>
    /// <param name="storedChunks">The cache resolving a chunk hash to its effective nonce.</param>
    /// <returns>The entries ordered by path, each carrying the resolved chunk nonces.</returns>
    private static async Task<IReadOnlyList<ChunkManifestFileEntry>> CanonicalizeChunkEntriesAsync(
        IEnumerable<ChunkManifestFileEntry> entries,
        ConcurrentDictionary<string, Lazy<Task<string>>> storedChunks
    )
    {
        List<ChunkManifestFileEntry> canonicalEntries = [];

        foreach (var entry in entries.OrderBy(static e => e.OriginalPath, StringComparer.Ordinal))
        {
            List<ChunkManifestChunkRef> canonicalChunks = [];

            foreach (var chunk in entry.Chunks)
            {
                var nonce = storedChunks.TryGetValue(chunk.Hash, out var storedChunk)
                    ? await storedChunk.Value.ConfigureAwait(false)
                    : chunk.Nonce;

                canonicalChunks.Add(new ChunkManifestChunkRef(chunk.Hash, chunk.Size, nonce));
            }

            canonicalEntries.Add(
                new ChunkManifestFileEntry(
                    entry.OriginalPath,
                    entry.FileHash,
                    entry.TotalSize,
                    canonicalChunks
                )
            );
        }

        return canonicalEntries;
    }

    /// <summary>
    /// Deletes the chunk files in the chunks directory that the newly saved manifest no longer
    /// references.
    /// </summary>
    /// <remarks>
    /// Pruning is best-effort cleanup that runs only after the manifest has been written, so nothing
    /// it does can lose data: a chunk that cannot be removed because it is locked or access is denied
    /// is left behind as a harmless orphan, and any other failure is reported rather than failing an
    /// update that has already completed.
    /// </remarks>
    /// <param name="chunksDir">The directory holding the stored chunk files.</param>
    /// <param name="referencedChunkHashes">The Base64 chunk hashes the new manifest still references.</param>
    /// <param name="namingKey">The sub-key each chunk's on-disk file name is derived from.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// <see langword="true"/> if every unreferenced chunk file was removed; <see langword="false"/> if
    /// one or more orphans were left behind.
    /// </returns>
    private async Task<bool> TryDeleteOrphanedChunksAsync(
        string chunksDir,
        IEnumerable<string> referencedChunkHashes,
        byte[] namingKey,
        CancellationToken cancellationToken
    )
    {
        try
        {
            HashSet<string> expectedFileNames = new(StringComparer.OrdinalIgnoreCase);
            foreach (var hash in referencedChunkHashes)
            {
                var hashBytes = DecodeBase64FixedLength(hash, SHA256.HashSizeInBytes, "Invalid chunk hash.");
                try
                {
                    var fileName = ComputeChunkFileNameWithExtension(namingKey, hashBytes);
                    _ = expectedFileNames.Add(fileName);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(hashBytes);
                }
            }

            if (!fileOperationsService.DirectoryExists(chunksDir))
            {
                return true;
            }

            var existingFiles = await fileOperationsService
                .GetFilesAsync(chunksDir, "*" + BackupConstants.AppFileExtension, cancellationToken)
                .ConfigureAwait(false);

            var pruned = true;

            foreach (var file in existingFiles)
            {
                var fileName = Path.GetFileName(file);
                if (!expectedFileNames.Contains(fileName))
                {
                    pruned &= fileOperationsService.TryDeleteFile(file);
                }
            }

            return pruned;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return false;
        }
    }

    /// <summary>
    /// Resolves the compression strategy for a mode, treating <see cref="CompressionMode.None"/> as
    /// no strategy so chunks bypass the compression path entirely.
    /// </summary>
    /// <param name="compressionMode">The compression mode recorded for the backup.</param>
    /// <returns>The compression strategy, or <see langword="null"/> when compression is disabled.</returns>
    private ICompressionStrategy? CreateCompressionStrategy(CompressionMode compressionMode)
    {
        return compressionMode is CompressionMode.None
            ? null
            : compressionServiceFactory.Create(compressionMode);
    }

    /// <summary>
    /// Wraps a block of memory in a non-writable stream, reusing the backing array when the memory
    /// exposes one so chunk data is not copied.
    /// </summary>
    /// <param name="data">The bytes to expose as a stream.</param>
    /// <returns>A read-only stream over the data.</returns>
    private static MemoryStream CreateReadOnlyStream(ReadOnlyMemory<byte> data)
    {
        return System.Runtime.InteropServices.MemoryMarshal.TryGetArray(data, out var segment)
            ? new MemoryStream(segment.Array!, segment.Offset, segment.Count, writable: false)
            : new MemoryStream(data.ToArray(), writable: false);
    }

    /// <summary>
    /// Determines whether a failure is confined to the file being processed, in which case the run
    /// records the error and moves on instead of aborting the whole operation.
    /// </summary>
    /// <remarks>
    /// <see cref="FileNotFoundException"/>, <see cref="DirectoryNotFoundException"/>, and
    /// <see cref="PathTooLongException"/> all derive from <see cref="IOException"/>. Invalid archive
    /// data is also confined to the manifest entry currently being processed.
    /// </remarks>
    /// <param name="ex">The exception thrown while processing a file.</param>
    /// <returns><see langword="true"/> if the failure affects only one file; otherwise <see langword="false"/>.</returns>
    private static bool IsFileLevelError(Exception ex)
    {
        return ex is IOException or InvalidDataException or UnauthorizedAccessException;
    }
}
