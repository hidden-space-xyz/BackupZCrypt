using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;

using BackupZCrypt.Application.Utilities.Helpers;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Application.ValueObjects.Manifest;
using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Backup;
using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Application.Services;

internal sealed partial class ChunkedBackupService
{
    /// <summary>
    /// Restores files from a chunked backup, decrypting and reassembling chunks in parallel and
    /// verifying each restored file's size and hash against the manifest.
    /// </summary>
    /// <remarks>
    /// The destination folder is created only once the manifest has been opened, so a wrong password
    /// leaves nothing behind. Each entry is checked against the running system before anything is
    /// written: a name the system cannot create, a name that clashes with another restored file once
    /// letter case is ignored, or a target inside the backup folder itself is reported for that file
    /// while every other file is still restored.
    /// </remarks>
    /// <param name="sourcePath">The directory containing the backup chunks and manifest.</param>
    /// <param name="destinationPath">The directory into which files are reconstructed.</param>
    /// <param name="request">The backup request carrying the password used to decrypt the manifest.</param>
    /// <param name="progress">A sink that receives incremental status updates.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A result describing the restore outcome; a wrong password surfaces as a failure result.</returns>
    public async Task<Result<BackupResult>> RestoreAsync(
        string sourcePath,
        string destinationPath,
        BackupRequest request,
        IProgress<BackupStatus> progress,
        CancellationToken cancellationToken
    )
    {
        var stopwatch = Stopwatch.StartNew();

        var (backup, openError) = await this.OpenBackupAsync(
                sourcePath,
                request.Password,
                MessageCode.ManifestRequiredForDecryption,
                MessageCode.InvalidPassword,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (backup is null)
        {
            return Result<BackupResult>.Failure(openError!);
        }

        using (backup)
        {
            var manifest = backup.Manifest;
            var reader = this.CreateChunkReader(backup, sourcePath);

            ValidateManifestEntries(manifest.Files);

            ConcurrentBag<LocalizableMessage> errors = [];
            var plan = PlanRestore(manifest, sourcePath, destinationPath, errors);

            await fileOperationsService
                .CreateDirectoryAsync(destinationPath, cancellationToken)
                .ConfigureAwait(false);

            await this.RestoreDirectoriesAsync(
                    manifest.Directories,
                    sourcePath,
                    destinationPath,
                    errors,
                    cancellationToken
                )
                .ConfigureAwait(false);

            var totalFiles = manifest.Files.Count;
            ProgressTracker tracker = new(
                progress,
                stopwatch,
                totalFiles,
                plan.Sum(static item => item.Entry.TotalSize)
            );
            tracker.ReportStart();

            long restoredBytes = 0;
            var processedFiles = 0;
            LocalizableMessage? fatalError = null;

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                await Parallel
                    .ForEachAsync(
                        plan,
                        new ParallelOptions
                        {
                            MaxDegreeOfParallelism = MaximumParallelFileOperations,
                            CancellationToken = linkedCts.Token,
                        },
                        async (item, token) =>
                        {
                            try
                            {
                                await this.RestoreFileFromChunksAsync(
                                        item.Entry,
                                        item.TargetPath,
                                        destinationPath,
                                        reader,
                                        tracker,
                                        token
                                    )
                                    .ConfigureAwait(false);

                                _ = Interlocked.Increment(ref processedFiles);
                                _ = Interlocked.Add(ref restoredBytes, item.Entry.TotalSize);
                                tracker.CompleteFile();
                            }
                            catch (Exception ex)
                                when (ex is not OperationCanceledException
                                    && (ex is CryptographicException || IsFileLevelError(ex)))
                            {
                                errors.Add(
                                    new LocalizableMessage(
                                        MessageCode.DecryptionErrorFormat,
                                        item.Entry.OriginalPath,
                                        FailureReason.From(ex)
                                    )
                                );
                            }
                            catch (Exception ex) when (ex is not OperationCanceledException)
                            {
                                _ = Interlocked.CompareExchange(
                                    ref fatalError,
                                    new LocalizableMessage(
                                        MessageCode.UnexpectedErrorFormat,
                                        ex.Message
                                    ),
                                    null
                                );
                                await linkedCts.CancelAsync().ConfigureAwait(false);
                            }
                        }
                    )
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (fatalError is not null)
            {
                return Result<BackupResult>.Failure(fatalError);
            }

            List<LocalizableMessage> errorList = OrderMessages(errors);
            stopwatch.Stop();

            return errorList.Count > 0 && processedFiles is 0
                ? Result<BackupResult>.Failure(
                    [new LocalizableMessage(MessageCode.AllFilesFailed), .. errorList]
                )
                : Result<BackupResult>.Success(
                    new BackupResult(
                        stopwatch.Elapsed,
                        restoredBytes,
                        processedFiles,
                        totalFiles,
                        errors: errorList
                    )
                );
        }
    }

    /// <summary>
    /// Verifies the integrity of a chunked backup without restoring any file. After the manifest is
    /// decrypted (a wrong password surfaces as a failure), every file is processed in parallel: its
    /// chunks are decrypted, authenticated, decompressed, and re-hashed against the manifest, with
    /// the reconstructed bytes discarded to <see cref="Stream.Null"/>. Per-file failures (missing or
    /// corrupted chunks, size or hash mismatches) are collected rather than aborting the run, so the
    /// result reports every affected file.
    /// </summary>
    /// <remarks>
    /// A chunk file whose content fails authentication is renamed with the
    /// <see cref="BackupConstants.QuarantineExtension"/> extension once the run has finished. The
    /// manifest then simply misses that chunk, which the next update regenerates from the source,
    /// while the damaged bytes stay on disk until that update prunes them.
    /// </remarks>
    /// <param name="sourcePath">The directory containing the backup chunks and manifest.</param>
    /// <param name="request">The backup request carrying the password used to decrypt the manifest.</param>
    /// <param name="progress">A sink that receives incremental status updates.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A result whose value reports how many files verified successfully and any integrity errors.</returns>
    public async Task<Result<BackupResult>> VerifyAsync(
        string sourcePath,
        BackupRequest request,
        IProgress<BackupStatus> progress,
        CancellationToken cancellationToken
    )
    {
        var stopwatch = Stopwatch.StartNew();

        var (backup, openError) = await this.OpenBackupAsync(
                sourcePath,
                request.Password,
                MessageCode.ManifestRequiredForVerify,
                MessageCode.VerifyInvalidPassword,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (backup is null)
        {
            return Result<BackupResult>.Failure(openError!);
        }

        using (backup)
        {
            var manifest = backup.Manifest;
            var reader = this.CreateChunkReader(backup, sourcePath);

            ValidateManifestEntries(manifest.Files);

            var totalFiles = manifest.Files.Count;
            ProgressTracker tracker = new(
                progress,
                stopwatch,
                totalFiles,
                manifest.Files.Sum(static f => f.TotalSize)
            );
            tracker.ReportStart();

            ConcurrentBag<LocalizableMessage> errors = [];
            ConcurrentDictionary<string, byte> damagedChunks = new(StringComparer.Ordinal);
            long verifiedBytes = 0;
            var processedFiles = 0;

            await Parallel
                .ForEachAsync(
                    manifest.Files,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = MaximumParallelFileOperations,
                        CancellationToken = cancellationToken,
                    },
                    async (fileEntry, token) =>
                    {
                        try
                        {
                            await this.VerifyFileChunksAsync(
                                    fileEntry,
                                    reader,
                                    Stream.Null,
                                    tracker,
                                    chunkFile => damagedChunks.TryAdd(chunkFile, 0),
                                    token
                                )
                                .ConfigureAwait(false);

                            _ = Interlocked.Increment(ref processedFiles);
                            _ = Interlocked.Add(ref verifiedBytes, fileEntry.TotalSize);
                            tracker.CompleteFile();
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            errors.Add(
                                new LocalizableMessage(
                                    MessageCode.IntegrityErrorFormat,
                                    fileEntry.OriginalPath,
                                    FailureReason.From(ex)
                                )
                            );
                        }
                    }
                )
                .ConfigureAwait(false);

            var setAside = this.QuarantineChunks(damagedChunks.Keys);
            stopwatch.Stop();

            return Result<BackupResult>.Success(
                new BackupResult(
                    stopwatch.Elapsed,
                    verifiedBytes,
                    processedFiles,
                    totalFiles,
                    errors: OrderMessages(errors),
                    warnings: setAside > 0
                        ? [new LocalizableMessage(MessageCode.DamagedChunksSetAsideFormat, setAside)]
                        : null
                )
            );
        }
    }

    /// <summary>
    /// Decides where each recorded file is restored, reporting the files that cannot be restored on
    /// this system instead of letting one of them fail the whole restore.
    /// </summary>
    /// <param name="manifest">The decrypted manifest.</param>
    /// <param name="backupRoot">The backup folder being read.</param>
    /// <param name="destinationPath">The restore root.</param>
    /// <param name="errors">The collector the per-file problems are added to.</param>
    /// <returns>The files to restore and their target paths, in manifest order.</returns>
    private static List<RestoreItem> PlanRestore(
        ChunkManifestData manifest,
        string backupRoot,
        string destinationPath,
        ConcurrentBag<LocalizableMessage> errors
    )
    {
        var backupPrefix = WithTrailingSeparator(Path.GetFullPath(backupRoot));
        Dictionary<string, string> claimedTargets = new(StringComparer.FromComparison(PathComparer));
        List<RestoreItem> plan = [];

        foreach (var entry in manifest.Files)
        {
            if (!ManifestPathPolicy.IsValidOnThisSystem(entry.OriginalPath))
            {
                errors.Add(
                    new LocalizableMessage(MessageCode.RestoreNameNotSupportedFormat, entry.OriginalPath)
                );
                continue;
            }

            string targetPath;
            try
            {
                targetPath = ManifestPathPolicy.ResolveSafeDestination(destinationPath, entry.OriginalPath);
            }
            catch (InvalidDataException exception)
            {
                errors.Add(
                    new LocalizableMessage(
                        MessageCode.DecryptionErrorFormat,
                        entry.OriginalPath,
                        FailureReason.From(exception)
                    )
                );
                continue;
            }

            if (targetPath.StartsWith(backupPrefix, PathComparer))
            {
                errors.Add(
                    new LocalizableMessage(MessageCode.RestoreTargetInsideBackupFormat, entry.OriginalPath)
                );
                continue;
            }

            if (claimedTargets.TryGetValue(targetPath, out var owner))
            {
                errors.Add(
                    new LocalizableMessage(MessageCode.RestoreNameConflictFormat, entry.OriginalPath, owner)
                );
                continue;
            }

            claimedTargets[targetPath] = entry.OriginalPath;
            plan.Add(new RestoreItem(entry, targetPath));
        }

        return plan;
    }

    /// <summary>
    /// Recreates the empty folders the manifest records, reporting any that cannot be created.
    /// </summary>
    /// <param name="directories">The recorded folders, or <see langword="null"/> when there are none.</param>
    /// <param name="backupRoot">The backup folder being read.</param>
    /// <param name="destinationPath">The restore root.</param>
    /// <param name="errors">The collector the per-folder problems are added to.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes once every folder has been handled.</returns>
    private async Task RestoreDirectoriesAsync(
        IReadOnlyList<string>? directories,
        string backupRoot,
        string destinationPath,
        ConcurrentBag<LocalizableMessage> errors,
        CancellationToken cancellationToken
    )
    {
        var backupPrefix = WithTrailingSeparator(Path.GetFullPath(backupRoot));

        foreach (var directory in directories ?? [])
        {
            if (!ManifestPathPolicy.IsValidOnThisSystem(directory))
            {
                errors.Add(new LocalizableMessage(MessageCode.RestoreNameNotSupportedFormat, directory));
                continue;
            }

            try
            {
                var targetPath = ManifestPathPolicy.ResolveSafeDestination(destinationPath, directory);

                if (targetPath.StartsWith(backupPrefix, PathComparer))
                {
                    errors.Add(
                        new LocalizableMessage(MessageCode.RestoreTargetInsideBackupFormat, directory)
                    );
                    continue;
                }

                ManifestPathPolicy.EnsureNoReparsePointDescendants(fileOperationsService, destinationPath, targetPath);
                await fileOperationsService
                    .CreateDirectoryAsync(targetPath, cancellationToken)
                    .ConfigureAwait(false);
                ManifestPathPolicy.EnsureNoReparsePointDescendants(fileOperationsService, destinationPath, targetPath);
            }
            catch (Exception exception)
                when (exception is not OperationCanceledException && IsFileLevelError(exception))
            {
                errors.Add(
                    new LocalizableMessage(
                        MessageCode.DecryptionErrorFormat,
                        directory,
                        FailureReason.From(exception)
                    )
                );
            }
        }
    }

    /// <summary>
    /// Renames damaged chunk files so the next update regenerates them. A chunk that cannot be
    /// renamed, for example on read-only media, is left as it is.
    /// </summary>
    /// <param name="chunkFiles">The chunk files that failed authentication.</param>
    /// <returns>The number of chunk files set aside.</returns>
    private int QuarantineChunks(IEnumerable<string> chunkFiles)
    {
        var count = 0;

        foreach (var chunkFile in chunkFiles.Order(StringComparer.Ordinal))
        {
            try
            {
                fileOperationsService.MoveFile(chunkFile, chunkFile + BackupConstants.QuarantineExtension);
                count++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }
        }

        return count;
    }

    /// <summary>
    /// Builds the reader that opens the chunks of an opened backup.
    /// </summary>
    /// <param name="backup">The opened backup.</param>
    /// <param name="backupRoot">The backup folder.</param>
    /// <returns>The chunk reader.</returns>
    private ChunkReader CreateChunkReader(OpenedBackup backup, string backupRoot)
    {
        return new ChunkReader(
            fileOperationsService.CombinePath(backupRoot, BackupConstants.ChunksDirectoryName),
            backup.Keys.ChunkEncryptionKey,
            backup.Keys.ChunkNonceKey,
            backup.Keys.NamingKey,
            encryptionServiceFactory.Create(backup.Manifest.Header.EncryptionAlgorithm),
            this.CreateCompressionStrategy(backup.Manifest.Header.Compression)
        );
    }

    /// <summary>
    /// Orders collected messages by the path they name, so a report reads the same on every run.
    /// </summary>
    /// <param name="messages">The collected messages.</param>
    /// <returns>The messages ordered by their first argument.</returns>
    private static List<LocalizableMessage> OrderMessages(IEnumerable<LocalizableMessage> messages)
    {
        return
        [
            .. messages.OrderBy(
                static m => m.Args.Count > 0 ? m.Args[0] as string ?? string.Empty : string.Empty,
                StringComparer.Ordinal
            ),
        ];
    }

    /// <summary>
    /// Appends a directory separator to a full path unless it already ends with one.
    /// </summary>
    /// <param name="path">The full path.</param>
    /// <returns>The path terminated by a directory separator.</returns>
    private static string WithTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
    }

    /// <summary>
    /// Reconstructs one backed-up file on disk, creating its parent directory, writing the decrypted
    /// chunks to a path confined to the restore root, and then applying the recorded modification time
    /// and attributes.
    /// </summary>
    /// <param name="fileEntry">The manifest entry describing the file and its chunks.</param>
    /// <param name="destFilePath">The resolved path the file is written to.</param>
    /// <param name="destinationPath">The restore root the file is written beneath.</param>
    /// <param name="reader">The reader that opens the backup's chunks.</param>
    /// <param name="tracker">The progress tracker that is told about every chunk written.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the file has been reconstructed and checked against the manifest.</returns>
    private async Task RestoreFileFromChunksAsync(
        ChunkManifestFileEntry fileEntry,
        string destFilePath,
        string destinationPath,
        ChunkReader reader,
        ProgressTracker tracker,
        CancellationToken cancellationToken
    )
    {
        var destDir = fileOperationsService.GetDirectoryName(destFilePath);

        if (!string.IsNullOrEmpty(destDir))
        {
            ManifestPathPolicy.EnsureNoReparsePointDescendants(fileOperationsService, destinationPath, destDir);
            await fileOperationsService
                .CreateDirectoryAsync(destDir, cancellationToken)
                .ConfigureAwait(false);
            ManifestPathPolicy.EnsureNoReparsePointDescendants(fileOperationsService, destinationPath, destDir);
        }

        await fileOperationsService
            .WriteFileAtomicallyAsync(
                destFilePath,
                (destStream, token) =>
                    this.VerifyFileChunksAsync(fileEntry, reader, destStream, tracker, null, token),
                cancellationToken
            )
            .ConfigureAwait(false);

        this.TryApplyMetadata(destFilePath, fileEntry);
    }

    /// <summary>
    /// Applies the modification time and attributes recorded for a restored file. The content is
    /// already in place, so a file system that cannot store them leaves the file as written.
    /// </summary>
    /// <param name="filePath">The restored file.</param>
    /// <param name="fileEntry">The manifest entry carrying the recorded metadata.</param>
    private void TryApplyMetadata(string filePath, ChunkManifestFileEntry fileEntry)
    {
        try
        {
            fileOperationsService.ApplyFileMetadata(
                filePath,
                fileEntry.LastWriteTimeUtc,
                fileEntry.Attributes?.HasFlag(ManifestFileAttributes.ReadOnly),
                fileEntry.Attributes?.HasFlag(ManifestFileAttributes.Hidden),
                fileEntry.UnixMode
            );
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or ArgumentException or PlatformNotSupportedException)
        {
            return;
        }
    }

    /// <summary>
    /// Decrypts and authenticates every chunk of a file into <paramref name="destination"/>, then
    /// checks the reassembled size and SHA-256 hash against the manifest.
    /// </summary>
    /// <remarks>
    /// Each chunk is authenticated with its content hash concatenated with its nonce as associated
    /// data, so a tampered chunk fails before any of it reaches <paramref name="destination"/>, and
    /// decompression is bounded by the size the manifest declares for the chunk so it cannot expand
    /// past it. Verification passes <see cref="Stream.Null"/> as the destination to discard the
    /// plaintext. Each chunk's decoded hash, nonce, associated data, ciphertext, and plaintext are
    /// zeroed once the chunk has been written, and the expected and computed file hashes are zeroed
    /// after they are compared.
    /// </remarks>
    /// <param name="fileEntry">The manifest entry describing the file and its chunks.</param>
    /// <param name="reader">The reader that opens the backup's chunks.</param>
    /// <param name="destination">The stream the reconstructed plaintext is written to.</param>
    /// <param name="tracker">The progress tracker that is told about every chunk processed.</param>
    /// <param name="onDamagedChunk">Told about a chunk file whose content fails authentication, if not <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when every chunk has been verified and written to <paramref name="destination"/>.</returns>
    /// <exception cref="MissingChunkException">A chunk file is missing.</exception>
    /// <exception cref="InvalidDataException">The entry path is malformed or a chunk decompresses beyond the declared size.</exception>
    /// <exception cref="CryptographicException">A chunk fails authentication or the size or hash does not match the manifest.</exception>
    private async Task VerifyFileChunksAsync(
        ChunkManifestFileEntry fileEntry,
        ChunkReader reader,
        Stream destination,
        ProgressTracker tracker,
        Action<string>? onDamagedChunk,
        CancellationToken cancellationToken
    )
    {
        ManifestPathPolicy.ValidateRelative(fileEntry.OriginalPath);

        using var fileHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long processedBytes = 0;

        foreach (var chunkRef in fileEntry.Chunks)
        {
            byte[]? chunkHash = null;
            byte[]? nonce = null;
            byte[]? encryptedData = null;
            byte[]? associatedData = null;
            byte[]? decryptedData = null;
            string? chunkFilePath = null;

            try
            {
                chunkHash = DecodeBase64FixedLength(
                    chunkRef.Hash,
                    SHA256.HashSizeInBytes,
                    "Invalid chunk hash."
                );
                nonce = ChunkCryptoHelper.ComputeChunkNonce(reader.NonceKey, chunkHash);
                chunkFilePath = this.ComputeChunkFilePath(reader.ChunksDirectory, reader.NamingKey, chunkHash);

                if (!fileOperationsService.FileExists(chunkFilePath))
                {
                    throw new MissingChunkException();
                }

                var storedSize = fileOperationsService.GetFileSize(chunkFilePath);
                var maximumStoredSize = reader.CompressionStrategy is null
                    ? checked(chunkRef.Size + EncryptionConstants.TagSize)
                    : MaximumStoredChunkSize;

                if (
                    storedSize < EncryptionConstants.TagSize
                    || storedSize > maximumStoredSize
                    || (reader.CompressionStrategy is null && storedSize != maximumStoredSize)
                )
                {
                    throw new InvalidDataException("Stored chunk size is invalid.");
                }

                encryptedData = await fileOperationsService
                    .ReadAllBytesBoundedAsync(chunkFilePath, maximumStoredSize, cancellationToken)
                    .ConfigureAwait(false);

                associatedData = ChunkCryptoHelper.BuildChunkAssociatedData(chunkHash, nonce);
                decryptedData = reader.EncryptionStrategy.DecryptChunk(
                    encryptedData,
                    reader.EncryptionKey,
                    nonce,
                    associatedData
                );

                if (reader.CompressionStrategy is not null)
                {
                    await using MemoryStream compressedStream = new(decryptedData, writable: false);

                    await using var decompressedStream = await reader.CompressionStrategy
                        .DecompressAsync(compressedStream, cancellationToken)
                        .ConfigureAwait(false);

                    var decompressedSize = await CopyToWithHashAsync(
                            decompressedStream,
                            destination,
                            fileHasher,
                            chunkRef.Size,
                            cancellationToken
                        )
                        .ConfigureAwait(false);

                    if (decompressedSize != chunkRef.Size)
                    {
                        throw new CryptographicException(
                            "Decompressed chunk size does not match the manifest."
                        );
                    }

                    processedBytes = checked(processedBytes + decompressedSize);
                }
                else
                {
                    if (decryptedData.Length != chunkRef.Size)
                    {
                        throw new CryptographicException(
                            "Chunk size does not match the manifest."
                        );
                    }

                    fileHasher.AppendData(decryptedData);
                    await destination
                        .WriteAsync(decryptedData, cancellationToken)
                        .ConfigureAwait(false);
                    processedBytes = checked(processedBytes + decryptedData.Length);
                }

                tracker.AddBytes(chunkRef.Size);
            }
            catch (Exception exception)
                when (chunkFilePath is not null
                    && exception is CryptographicException or InvalidDataException)
            {
                onDamagedChunk?.Invoke(chunkFilePath);
                throw;
            }
            finally
            {
                if (chunkHash is not null)
                {
                    CryptographicOperations.ZeroMemory(chunkHash);
                }

                if (nonce is not null)
                {
                    CryptographicOperations.ZeroMemory(nonce);
                }

                if (associatedData is not null)
                {
                    CryptographicOperations.ZeroMemory(associatedData);
                }

                if (encryptedData is not null)
                {
                    CryptographicOperations.ZeroMemory(encryptedData);
                }

                if (decryptedData is not null)
                {
                    CryptographicOperations.ZeroMemory(decryptedData);
                }
            }
        }

        if (processedBytes != fileEntry.TotalSize)
        {
            throw new CryptographicException("File size does not match the manifest.");
        }

        var expectedFileHash = DecodeBase64FixedLength(
            fileEntry.FileHash,
            SHA256.HashSizeInBytes,
            "Invalid file hash."
        );
        var actualFileHash = fileHasher.GetHashAndReset();

        try
        {
            if (!CryptographicOperations.FixedTimeEquals(expectedFileHash, actualFileHash))
            {
                throw new CryptographicException("File hash does not match the manifest.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expectedFileHash);
            CryptographicOperations.ZeroMemory(actualFileHash);
        }
    }

    /// <summary>
    /// Copies a stream to a destination while appending the copied bytes to a running hash, stopping
    /// with an error if the copy would exceed the caller's byte budget.
    /// </summary>
    /// <remarks>
    /// The budget bounds decompression to the size the manifest declares for the chunk, so a crafted
    /// chunk cannot expand without limit. The pooled buffer is zeroed before it is returned.
    /// </remarks>
    /// <param name="source">The stream to read from.</param>
    /// <param name="destination">The stream the bytes are written to.</param>
    /// <param name="hasher">The running hash the copied bytes are appended to.</param>
    /// <param name="maxBytes">The maximum number of bytes this copy is allowed to produce.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The number of bytes copied.</returns>
    /// <exception cref="InvalidDataException">The source yields more than <paramref name="maxBytes"/> bytes.</exception>
    private static async Task<long> CopyToWithHashAsync(
        Stream source,
        Stream destination,
        IncrementalHash hasher,
        long maxBytes,
        CancellationToken cancellationToken
    )
    {
        var buffer = ArrayPool<byte>.Shared.Rent(StreamConstants.CopyBufferSize);
        long total = 0;

        try
        {
            while (true)
            {
                var read = await source
                    .ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                    .ConfigureAwait(false);

                if (read is 0)
                {
                    return total;
                }

                total += read;

                if (total > maxBytes)
                {
                    throw new InvalidDataException(
                        "Decompressed data exceeds the size declared in the manifest."
                    );
                }

                hasher.AppendData(buffer.AsSpan(0, read));
                await destination
                    .WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer.AsSpan(0, buffer.Length));
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// A recorded file a restore writes, and where.
    /// </summary>
    /// <param name="Entry">The manifest entry.</param>
    /// <param name="TargetPath">The resolved path the file is written to.</param>
    private sealed record class RestoreItem(ChunkManifestFileEntry Entry, string TargetPath);

    /// <summary>
    /// Everything needed to open the chunks of one backup.
    /// </summary>
    /// <param name="ChunksDirectory">The directory holding the chunk files.</param>
    /// <param name="EncryptionKey">The chunk encryption sub-key.</param>
    /// <param name="NonceKey">The sub-key each chunk's nonce is derived from.</param>
    /// <param name="NamingKey">The sub-key each chunk's on-disk file name is derived from.</param>
    /// <param name="EncryptionStrategy">The strategy used to decrypt chunks.</param>
    /// <param name="CompressionStrategy">The decompression strategy, or <see langword="null"/> if chunks are uncompressed.</param>
    private sealed record class ChunkReader(
        string ChunksDirectory,
        byte[] EncryptionKey,
        byte[] NonceKey,
        byte[] NamingKey,
        IEncryptionAlgorithmStrategy EncryptionStrategy,
        ICompressionStrategy? CompressionStrategy
    );
}
