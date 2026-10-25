using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

using BackupZCrypt.Application.Algorithms;
using BackupZCrypt.Application.FileSystem;
using BackupZCrypt.Domain.Archive;

namespace BackupZCrypt.Application.Engine;

/// <summary>
/// The chunk store of a backup folder on the write side: it splits files into content-defined
/// chunks, compresses, encrypts, and writes each chunk once under its keyed name, deduplicating
/// identical content, and prunes or deletes chunk files a run no longer needs.
/// </summary>
/// <param name="fileOperationsService">The service used to read source files and write chunk files.</param>
/// <param name="chunkingStrategy">The strategy used to split file streams into content-defined chunks.</param>
internal sealed class ChunkStore(
    IFileOperationsService fileOperationsService,
    IChunkingStrategy chunkingStrategy
)
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
    /// <param name="cipher">The archive's chunk sealer and naming sub-key.</param>
    /// <param name="storedChunks">The shared cache mapping a chunk hash to its in-flight or completed store operation.</param>
    /// <param name="context">The run's chunk slots and the record of the chunk files it wrote.</param>
    /// <param name="tracker">The progress tracker that is told about every chunk read.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The manifest entry describing the file and its ordered chunk references.</returns>
    internal async Task<ChunkManifestFileEntry> ChunkAndEncryptFileAsync(
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
                return ManifestEntries.WithMetadata(
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
    /// <param name="cipher">The archive's chunk sealer and naming sub-key.</param>
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
            var chunkFilePath = BackupLayout.ChunkFilePath(chunksDir, cipher.NamingKey, chunkHash);
            var ownedHash = chunkHash;

            var chunkOperation = new Lazy<Task>(
                () =>
                    this.SealAndStoreChunkAsync(
                        chunkData,
                        ownedHash,
                        chunkFilePath,
                        cipher.Sealer,
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
    /// Seals a chunk and writes the stored bytes to its content-addressed file.
    /// </summary>
    /// <remarks>
    /// The stored bytes are zeroed once they are on disk; the plaintext and the chunk hash are owned
    /// by the caller.
    /// </remarks>
    /// <param name="chunkData">The plaintext bytes of the chunk.</param>
    /// <param name="chunkHash">The SHA-256 content hash of the chunk.</param>
    /// <param name="chunkFilePath">The full path the sealed chunk is written to.</param>
    /// <param name="sealer">The sealer that turns the chunk into its stored bytes.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes once the chunk file is on disk.</returns>
    private async Task SealAndStoreChunkAsync(
        ReadOnlyMemory<byte> chunkData,
        byte[] chunkHash,
        string chunkFilePath,
        ChunkSealer sealer,
        CancellationToken cancellationToken
    )
    {
        var storedChunk = await sealer
            .SealAsync(chunkData, chunkHash, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await fileOperationsService
                .WriteAllBytesAtomicallyAsync(chunkFilePath, storedChunk, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(storedChunk);
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
    internal static ConcurrentDictionary<string, Lazy<Task>> BuildStoredChunkCache(
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
    /// impossible stored size, so an update regenerates them from the source.
    /// </summary>
    /// <param name="storedChunks">The preloaded deduplication cache to filter.</param>
    /// <param name="manifestFiles">The existing manifest entries that describe stored chunks.</param>
    /// <param name="chunksDir">The archive directory that should contain the chunk files.</param>
    /// <param name="cipher">The archive's chunk sealer and naming sub-key.</param>
    internal void RemoveUnavailableStoredChunks(
        ConcurrentDictionary<string, Lazy<Task>> storedChunks,
        IReadOnlyList<ChunkManifestFileEntry> manifestFiles,
        string chunksDir,
        ChunkCipherSet cipher
    )
    {
        HashSet<string> inspectedHashes = new(StringComparer.Ordinal);

        foreach (
            var chunk in manifestFiles
                .SelectMany(static file => file.Chunks)
                .Where(chunk =>
                    inspectedHashes.Add(chunk.Hash)
                    && !this.IsStoredChunkAvailable(chunk, chunksDir, cipher)
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
    /// <param name="chunksDir">The directory expected to contain its stored bytes.</param>
    /// <param name="cipher">The archive's chunk sealer and naming sub-key.</param>
    /// <returns><see langword="true"/> when the entry is a regular file of a plausible size.</returns>
    private bool IsStoredChunkAvailable(
        ChunkManifestChunkRef chunk,
        string chunksDir,
        ChunkCipherSet cipher
    )
    {
        byte[]? chunkHash = null;

        try
        {
            chunkHash = ManifestEntries.DecodeBase64FixedLength(
                chunk.Hash,
                SHA256.HashSizeInBytes,
                "Invalid chunk hash."
            );
            var chunkFilePath = BackupLayout.ChunkFilePath(chunksDir, cipher.NamingKey, chunkHash);

            if (
                !fileOperationsService.FileExists(chunkFilePath)
                || fileOperationsService.IsReparsePoint(chunkFilePath)
            )
            {
                return false;
            }

            return cipher.Sealer.IsPlausibleStoredSize(
                fileOperationsService.GetFileSize(chunkFilePath),
                chunk.Size
            );
        }
        catch (Exception exception) when (FileFailures.IsConfinedToOneFile(exception))
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
    internal async Task<bool> TryPruneBackupAsync(
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
                var hashBytes = ManifestEntries.DecodeBase64FixedLength(hash, SHA256.HashSizeInBytes, "Invalid chunk hash.");
                try
                {
                    var fileName = ChunkCrypto.ComputeChunkFileName(namingKey, hashBytes);
                    _ = expectedFileNames.Add(fileName);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(hashBytes);
                }
            }

            var pruned = true;

            foreach (
                var name in fileOperationsService
                    .GetDirectoryEntryNames(backupRoot)
                    .Where(BackupLayout.IsTemporaryFileName)
            )
            {
                pruned &= fileOperationsService.TryDeleteFile(Path.Join(backupRoot, name));
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
    /// Creates the backup folder and its chunks directory, refusing a chunks directory reached
    /// through a link.
    /// </summary>
    /// <param name="destinationPath">The backup folder.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The full path of the chunks directory.</returns>
    internal async Task<string> PrepareChunksDirectoryAsync(
        string destinationPath,
        CancellationToken cancellationToken
    )
    {
        await fileOperationsService
            .CreateDirectoryAsync(destinationPath, cancellationToken)
            .ConfigureAwait(false);

        var chunksDir = Path.Join(
            destinationPath,
            BackupConstants.ChunksDirectoryName
        );

        RestorePathGuard.EnsureNoReparsePointDescendants(
            fileOperationsService,
            destinationPath,
            chunksDir
        );
        await fileOperationsService
            .CreateDirectoryAsync(chunksDir, cancellationToken)
            .ConfigureAwait(false);
        RestorePathGuard.EnsureNoReparsePointDescendants(
            fileOperationsService,
            destinationPath,
            chunksDir
        );

        return chunksDir;
    }

    /// <summary>
    /// Deletes every chunk file a run wrote, after the run failed or was cancelled before its manifest
    /// was published, and removes the chunks directory again when that leaves it empty.
    /// </summary>
    /// <remarks>
    /// The chunks of a run are named under its own keys, so none of them can belong to the backup the
    /// run was going to replace. Cleanup is best-effort: a file that cannot be deleted stays behind as
    /// an orphan the next successful run prunes.
    /// </remarks>
    /// <param name="context">The run's record of the chunk files it wrote.</param>
    /// <param name="chunksDir">The chunks directory.</param>
    internal void DeleteWrittenChunks(ChunkWriteContext context, string chunksDir)
    {
        foreach (var chunkFile in context.WrittenChunks)
        {
            _ = fileOperationsService.TryDeleteFile(chunkFile);
        }

        try
        {
            if (
                fileOperationsService.DirectoryExists(chunksDir)
                && fileOperationsService.GetDirectoryEntryNames(chunksDir).Count is 0
            )
            {
                fileOperationsService.DeleteEmptyDirectory(chunksDir);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }
    }

    /// <summary>
    /// A chunk of a file whose store has been started.
    /// </summary>
    /// <param name="Size">The plaintext length of the chunk.</param>
    /// <param name="Store">The store operation, resolving to the chunk's Base64-encoded hash.</param>
    private sealed record class PendingChunk(int Size, Task<string> Store);
}
