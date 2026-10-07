using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

using BackupZCrypt.Application.Utilities.Extensions;
using BackupZCrypt.Application.Utilities.Helpers;
using BackupZCrypt.Application.ValueObjects.Manifest;
using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Domain.ValueObjects.FileSystem;

namespace BackupZCrypt.Application.Services;

internal sealed partial class ChunkedBackupService
{
    /// <summary>
    /// Splits a file into content-defined chunks, stores each chunk encrypted on disk, hashes the
    /// whole file, and returns the manifest entry that reconstructs it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The file is read and hashed in order, while its chunks are compressed, encrypted, and written
    /// concurrently, each holding one of the run's chunk slots until it is on disk. A large file
    /// therefore uses every processor instead of one, and the number of chunk buffers alive at once
    /// stays bounded however many files are processed in parallel.
    /// </para>
    /// <para>
    /// Chunks are deduplicated through <paramref name="storedChunks"/>, so a chunk already being
    /// stored for this or another file is awaited instead of being encrypted and written a second
    /// time. The entry records the bytes actually read, so a file that grows or shrinks while it is
    /// read is captured consistently rather than failing the run.
    /// </para>
    /// </remarks>
    /// <param name="filePath">The absolute path of the file to read.</param>
    /// <param name="relativePath">The file's path relative to the backup root, as recorded in the manifest.</param>
    /// <param name="metadata">The file's metadata, read before its content and recorded in the entry.</param>
    /// <param name="chunksDir">The directory encrypted chunk files are written into.</param>
    /// <param name="cipher">The key material and strategies chunks are compressed and encrypted with.</param>
    /// <param name="storedChunks">The shared cache mapping a chunk hash to its in-flight or completed store operation.</param>
    /// <param name="context">The run's chunk slots and the record of the chunk files it wrote.</param>
    /// <param name="tracker">The progress tracker that is told about every chunk read.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The manifest entry describing the file and its ordered chunk references.</returns>
    private async Task<ChunkManifestFileEntry> ChunkAndEncryptFileAsync(
        string filePath,
        string relativePath,
        FileMetadata metadata,
        string chunksDir,
        ChunkCipherSet cipher,
        ConcurrentDictionary<string, Lazy<Task>> storedChunks,
        ChunkWriteContext context,
        ProgressTracker tracker,
        CancellationToken cancellationToken
    )
    {
        ManifestPathPolicy.ValidateRelative(relativePath);
        List<PendingChunk> pending = [];

        try
        {
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
                actualFileSize = checked(actualFileSize + chunkData.Length);
                fileHasher.AppendData(chunkData.Span);

                try
                {
                    await context.ChunkSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    ZeroChunk(chunkData, null);
                    throw;
                }

                pending.Add(
                    new PendingChunk(
                        chunkData.Length,
                        Task.Run(() =>
                            this.StoreChunkAsync(
                                chunkData,
                                chunksDir,
                                cipher,
                                storedChunks,
                                context,
                                cancellationToken
                            )
                        )
                    )
                );

                tracker.AddBytes(chunkData.Length);
            }

            List<ChunkManifestChunkRef> chunkRefs = new(pending.Count);
            foreach (var chunk in pending)
            {
                chunkRefs.Add(new ChunkManifestChunkRef(await chunk.Store.ConfigureAwait(false), chunk.Size));
            }

            var fileHash = fileHasher.GetHashAndReset();

            try
            {
                return WithMetadata(
                    new ChunkManifestFileEntry(
                        relativePath,
                        Convert.ToBase64String(fileHash),
                        actualFileSize,
                        chunkRefs,
                        metadata.LastWriteTimeUtc
                    ),
                    metadata
                );
            }
            finally
            {
                CryptographicOperations.ZeroMemory(fileHash);
            }
        }
        catch
        {
            await AwaitQuietlyAsync(pending).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Hashes one chunk and stores it, or joins the store already under way for identical content,
    /// then releases the chunk's slot and wipes its plaintext once the chunk is on disk.
    /// </summary>
    /// <remarks>
    /// The chunk hash is computed here, on a pool thread, rather than by the reader, so a large file
    /// is read and chunked at the speed of the chunker alone while hashing, compression, and
    /// encryption of its chunks run in parallel.
    /// </remarks>
    /// <param name="chunkData">The plaintext bytes of the chunk, owned by this call.</param>
    /// <param name="chunksDir">The directory encrypted chunk files are written into.</param>
    /// <param name="cipher">The key material and strategies chunks are compressed and encrypted with.</param>
    /// <param name="storedChunks">The shared cache mapping a chunk hash to its in-flight or completed store operation.</param>
    /// <param name="context">The run's chunk slots and the record of the chunk files it wrote.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The Base64-encoded content hash of the chunk.</returns>
    private async Task<string> StoreChunkAsync(
        ReadOnlyMemory<byte> chunkData,
        string chunksDir,
        ChunkCipherSet cipher,
        ConcurrentDictionary<string, Lazy<Task>> storedChunks,
        ChunkWriteContext context,
        CancellationToken cancellationToken
    )
    {
        byte[]? chunkHash = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            chunkHash = SHA256.HashData(chunkData.Span);
            var chunkHashB64 = Convert.ToBase64String(chunkHash);
            var chunkFilePath = this.ComputeChunkFilePath(chunksDir, cipher.NamingKey, chunkHash);
            var ownedHash = chunkHash;

            var chunkOperation = new Lazy<Task>(
                () =>
                    this.EncryptAndStoreChunkAsync(
                        chunkData,
                        ownedHash,
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

            await AwaitStoredChunkAsync(chunkHashB64, storedChunk, chunkOperation, storedChunks)
                .ConfigureAwait(false);

            if (ReferenceEquals(storedChunk, chunkOperation))
            {
                context.WrittenChunks.Add(chunkFilePath);
            }

            return chunkHashB64;
        }
        finally
        {
            ZeroChunk(chunkData, chunkHash);
            _ = context.ChunkSlots.Release();
        }
    }

    /// <summary>
    /// Waits for every chunk store a failed file had started, ignoring their outcome, so none keeps
    /// running against the file's buffers or faults unobserved.
    /// </summary>
    /// <param name="pending">The chunk stores started for the file.</param>
    /// <returns>A task that completes once every store has finished.</returns>
    private static async Task AwaitQuietlyAsync(List<PendingChunk> pending)
    {
        foreach (var chunk in pending)
        {
            try
            {
                _ = await chunk.Store.ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                continue;
            }
        }
    }

    /// <summary>
    /// Wipes a chunk's plaintext and, when it was computed, its content hash.
    /// </summary>
    /// <param name="chunkData">The chunk's plaintext.</param>
    /// <param name="chunkHash">The chunk's content hash, or <see langword="null"/>.</param>
    private static void ZeroChunk(ReadOnlyMemory<byte> chunkData, byte[]? chunkHash)
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

    /// <summary>
    /// A chunk of a file whose store has been started.
    /// </summary>
    /// <param name="Size">The plaintext length of the chunk.</param>
    /// <param name="Store">The store operation, resolving to the chunk's Base64-encoded hash.</param>
    private sealed record class PendingChunk(int Size, Task<string> Store);

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
    /// <returns>A task that completes once the chunk file is on disk.</returns>
    private async Task EncryptAndStoreChunkAsync(
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
    /// Builds the deduplication cache of an update, holding one completed store operation for every
    /// chunk the existing manifest references.
    /// </summary>
    /// <remarks>
    /// The entries are shared with the write path, so a chunk the backup already holds is never
    /// encrypted or written again. Its nonce needs no recording: it follows from the chunk-nonce
    /// sub-key, which stays the same for the backup's whole lifetime because an update keeps the
    /// master salt, and from the chunk hash.
    /// </remarks>
    /// <param name="manifestFiles">The manifest entries whose chunk references are indexed.</param>
    /// <returns>A cache keyed by Base64 chunk hash.</returns>
    private static ConcurrentDictionary<string, Lazy<Task>> BuildStoredChunkCache(
        IReadOnlyList<ChunkManifestFileEntry> manifestFiles
    )
    {
        ConcurrentDictionary<string, Lazy<Task>> storedChunks = new(StringComparer.Ordinal);
        Lazy<Task> stored = new(Task.CompletedTask);

        foreach (var chunk in manifestFiles.SelectMany(static file => file.Chunks))
        {
            _ = storedChunks.TryAdd(chunk.Hash, stored);
        }

        return storedChunks;
    }

    /// <summary>
    /// Removes preloaded deduplication entries whose stored chunk is absent, linked, or has an
    /// impossible ciphertext size, so an update regenerates them from the source.
    /// </summary>
    /// <param name="storedChunks">The preloaded deduplication cache to filter.</param>
    /// <param name="manifestFiles">The existing manifest entries that describe stored chunks.</param>
    /// <param name="chunksDir">The archive directory that should contain the chunk files.</param>
    /// <param name="namingKey">The sub-key used to derive chunk file names.</param>
    /// <param name="compressionStrategy">The archive compression strategy, or <see langword="null"/>.</param>
    private void RemoveUnavailableStoredChunks(
        ConcurrentDictionary<string, Lazy<Task>> storedChunks,
        IReadOnlyList<ChunkManifestFileEntry> manifestFiles,
        string chunksDir,
        byte[] namingKey,
        ICompressionStrategy? compressionStrategy
    )
    {
        HashSet<string> inspectedHashes = new(StringComparer.Ordinal);

        foreach (
            var chunk in manifestFiles
                .SelectMany(static file => file.Chunks)
                .Where(chunk =>
                    inspectedHashes.Add(chunk.Hash)
                    && !this.IsStoredChunkAvailable(
                        chunk,
                        chunksDir,
                        namingKey,
                        compressionStrategy
                    )
                )
        )
        {
            _ = storedChunks.TryRemove(chunk.Hash, out _);
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
    private static void ValidateManifestEntries(
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
    /// Awaits the store operation for a chunk, evicting a failed operation from the cache so a later
    /// file re-attempts it instead of reusing a permanently faulted task.
    /// </summary>
    /// <remarks>
    /// Only the operation this caller published is evicted, identified by reference, so an entry a
    /// concurrent caller won the race with is never removed. The failure is always rethrown.
    /// </remarks>
    /// <param name="chunkHashB64">The Base64-encoded content hash keying the chunk in the cache.</param>
    /// <param name="storedChunk">The operation actually held in the cache, which may belong to another caller.</param>
    /// <param name="candidateChunk">The operation this caller offered when adding the entry.</param>
    /// <param name="storedChunks">The shared cache of chunk store operations.</param>
    /// <returns>A task that completes once the chunk is stored.</returns>
    private static async Task AwaitStoredChunkAsync(
        string chunkHashB64,
        Lazy<Task> storedChunk,
        Lazy<Task> candidateChunk,
        ConcurrentDictionary<string, Lazy<Task>> storedChunks
    )
    {
        try
        {
            await storedChunk.Value.ConfigureAwait(false);
        }
        catch
        {
            if (ReferenceEquals(storedChunk, candidateChunk))
            {
                _ = storedChunks.TryRemove(
                    new KeyValuePair<string, Lazy<Task>>(chunkHashB64, candidateChunk)
                );
            }

            throw;
        }
    }

    /// <summary>
    /// Deletes what a published backup no longer needs: chunk files the new manifest does not
    /// reference — including every chunk of a backup a create has just replaced — chunk files set
    /// aside by verification, and temporary files a crashed run left behind.
    /// </summary>
    /// <remarks>
    /// Pruning is best-effort cleanup that runs only after the manifest has been written, under the
    /// backup's exclusive lock, so nothing it does can lose data: a file that cannot be removed is
    /// left behind as a harmless orphan rather than failing an operation that has already completed.
    /// Only names this program produces are touched, so files a user placed in the backup folder are
    /// never deleted.
    /// </remarks>
    /// <param name="backupRoot">The backup folder holding the manifest.</param>
    /// <param name="chunksDir">The directory holding the stored chunk files.</param>
    /// <param name="referencedChunkHashes">The Base64 chunk hashes the new manifest references.</param>
    /// <param name="namingKey">The sub-key each chunk's on-disk file name is derived from.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// <see langword="true"/> if every unneeded file was removed; <see langword="false"/> if one or
    /// more were left behind.
    /// </returns>
    private async Task<bool> TryPruneBackupAsync(
        string backupRoot,
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

            var pruned = true;

            foreach (var name in fileOperationsService.GetDirectoryEntryNames(backupRoot))
            {
                if (BackupLayout.IsTemporaryFileName(name))
                {
                    pruned &= fileOperationsService.TryDeleteFile(
                        fileOperationsService.CombinePath(backupRoot, name)
                    );
                }
            }

            if (!fileOperationsService.DirectoryExists(chunksDir))
            {
                return pruned;
            }

            var existingFiles = await fileOperationsService
                .GetFilesAsync(chunksDir, "*", cancellationToken)
                .ConfigureAwait(false);

            foreach (var file in existingFiles)
            {
                var fileName = Path.GetFileName(file);
                var unneeded =
                    (BackupLayout.IsChunkFileName(fileName) && !expectedFileNames.Contains(fileName))
                    || BackupLayout.IsQuarantinedChunkFileName(fileName)
                    || BackupLayout.IsTemporaryFileName(fileName);

                if (unneeded)
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
