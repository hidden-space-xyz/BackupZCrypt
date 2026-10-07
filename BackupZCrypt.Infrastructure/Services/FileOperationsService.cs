using System.IO.Enumeration;
using System.Security.Cryptography;

using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Services.Interfaces;
using BackupZCrypt.Domain.ValueObjects.FileSystem;

namespace BackupZCrypt.Infrastructure.Services;

/// <summary>
/// File-system implementation of <see cref="IFileOperationsService"/> backed by
/// <see cref="System.IO"/>. Stream factory methods open files asynchronously with
/// sequential-scan hints, and <see cref="ComputeFileHashAsync"/> streams through the shared
/// <see cref="StreamConstants.CopyBufferSize"/>.
/// </summary>
internal sealed class FileOperationsService : IFileOperationsService
{
    /// <summary>
    /// Enumerates files recursively under <paramref name="directoryPath"/> that match
    /// <paramref name="searchPattern"/>, skipping inaccessible entries. Recursion stops at
    /// reparse points (symlinks/junctions), so traversal cannot cycle, descend outside the source
    /// tree, or copy the contents of a file reached through a link.
    /// </summary>
    /// <param name="directoryPath">The root directory to enumerate.</param>
    /// <param name="searchPattern">A simple wildcard expression matched against file names; defaults to all files.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>The full paths of the matching files.</returns>
    public async Task<string[]> GetFilesAsync(
        string directoryPath,
        string searchPattern = "*",
        CancellationToken cancellationToken = default
    )
    {
        return await Task.Run(
            () =>
            {
                FileSystemEnumerable<string> enumerable = new(
                    directoryPath,
                    static (ref entry) => entry.ToFullPath(),
                    new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible = true,
                        AttributesToSkip = FileAttributes.None,
                    }
                )
                {
                    ShouldIncludePredicate = (ref entry) =>
                        !entry.IsDirectory
                        && !entry.Attributes.HasFlag(FileAttributes.ReparsePoint)
                        && FileSystemName.MatchesSimpleExpression(searchPattern, entry.FileName),
                    ShouldRecursePredicate = static (ref entry) =>
                        !entry.Attributes.HasFlag(FileAttributes.ReparsePoint),
                };

                return enumerable.ToArray();
            },
            cancellationToken
        );
    }

    /// <summary>
    /// Walks the tree under <paramref name="directoryPath"/> on a background thread, one folder at a
    /// time, so a folder that cannot be listed is recorded and skipped instead of hiding the rest of
    /// the tree or failing the whole walk.
    /// </summary>
    /// <remarks>
    /// Only symbolic links and junctions are left out. Other reparse points — cloud placeholders,
    /// deduplicated or compacted files — hold the file's own data and are walked like any other entry.
    /// </remarks>
    /// <param name="directoryPath">The root directory to walk.</param>
    /// <param name="cancellationToken">A token to cancel the walk.</param>
    /// <returns>The files and folders found and the entries that were skipped.</returns>
    public Task<DirectoryScan> ScanDirectoryAsync(
        string directoryPath,
        CancellationToken cancellationToken = default
    )
    {
        return Task.Run(() => ScanDirectory(directoryPath, cancellationToken), cancellationToken);
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> GetDirectoryEntryNames(string directoryPath)
    {
        return
        [
            .. new FileSystemEnumerable<string>(
                directoryPath,
                static (ref entry) => entry.FileName.ToString(),
                new EnumerationOptions
                {
                    RecurseSubdirectories = false,
                    IgnoreInaccessible = false,
                    AttributesToSkip = FileAttributes.None,
                }
            ),
        ];
    }

    /// <inheritdoc/>
    public bool DirectoryExists(string directoryPath)
    {
        return Directory.Exists(directoryPath);
    }

    /// <inheritdoc/>
    public bool FileExists(string filePath)
    {
        return File.Exists(filePath);
    }

    /// <inheritdoc/>
    public bool IsReparsePoint(string path)
    {
        return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
    }

    /// <summary>
    /// Creates the specified directory (and any missing parents) on a background thread.
    /// </summary>
    /// <param name="directoryPath">The directory path to create.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the directory has been created.</returns>
    public async Task CreateDirectoryAsync(
        string directoryPath,
        CancellationToken cancellationToken = default
    )
    {
        _ = await Task.Run(() => Directory.CreateDirectory(directoryPath), cancellationToken);
    }

    /// <inheritdoc/>
    public void DeleteEmptyDirectory(string directoryPath)
    {
        Directory.Delete(directoryPath, recursive: false);
    }

    /// <inheritdoc/>
    public void DeleteFile(string filePath)
    {
        File.Delete(filePath);
    }

    /// <inheritdoc/>
    public void MoveFile(string sourcePath, string destinationPath)
    {
        File.Move(sourcePath, destinationPath, overwrite: true);
    }

    /// <summary>
    /// Returns the size, in bytes, of the specified file.
    /// </summary>
    /// <param name="filePath">The path of the file to measure.</param>
    /// <returns>The file length in bytes.</returns>
    public long GetFileSize(string filePath)
    {
        return new FileInfo(filePath).Length;
    }

    /// <summary>
    /// Reads the length, modification time, and attributes of a file. The hidden flag is recorded only
    /// on Windows, where it is a real attribute rather than a reflection of a leading dot, and the
    /// permission bits only on Unix.
    /// </summary>
    /// <param name="filePath">The path of the file to inspect.</param>
    /// <returns>The file's metadata.</returns>
    public FileMetadata GetFileMetadata(string filePath)
    {
        var info = new FileInfo(filePath);
        var attributes = info.Attributes;

        return new FileMetadata(
            info.Length,
            info.LastWriteTimeUtc,
            attributes.HasFlag(FileAttributes.ReadOnly),
            OperatingSystem.IsWindows() && attributes.HasFlag(FileAttributes.Hidden),
            OperatingSystem.IsWindows() ? null : (int)File.GetUnixFileMode(filePath)
        );
    }

    /// <summary>
    /// Applies the recorded modification time first, while the file is still writable, and the
    /// attributes afterwards. On Unix the permission bits take precedence over the read-only flag,
    /// which they already express.
    /// </summary>
    /// <param name="filePath">The path of the restored file.</param>
    /// <param name="lastWriteTimeUtc">The modification time to apply, or <see langword="null"/>.</param>
    /// <param name="isReadOnly">Whether to mark the file read-only, or <see langword="null"/>.</param>
    /// <param name="isHidden">Whether to mark the file hidden, or <see langword="null"/>.</param>
    /// <param name="unixMode">The Unix permission bits to apply, or <see langword="null"/>.</param>
    public void ApplyFileMetadata(
        string filePath,
        DateTime? lastWriteTimeUtc,
        bool? isReadOnly,
        bool? isHidden,
        int? unixMode
    )
    {
        if (lastWriteTimeUtc is { } lastWrite)
        {
            File.SetLastWriteTimeUtc(filePath, DateTime.SpecifyKind(lastWrite, DateTimeKind.Utc));
        }

        if (!OperatingSystem.IsWindows())
        {
            if (unixMode is { } mode)
            {
                File.SetUnixFileMode(filePath, (UnixFileMode)mode & (UnixFileMode)0x1FF);
            }
            else if (isReadOnly is true)
            {
                File.SetUnixFileMode(
                    filePath,
                    File.GetUnixFileMode(filePath)
                        & ~(UnixFileMode.UserWrite | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)
                );
            }

            return;
        }

        var attributes = File.GetAttributes(filePath);
        var updated = attributes;

        if (isHidden is { } hidden)
        {
            updated = hidden ? updated | FileAttributes.Hidden : updated & ~FileAttributes.Hidden;
        }

        if (isReadOnly is { } readOnly)
        {
            updated = readOnly ? updated | FileAttributes.ReadOnly : updated & ~FileAttributes.ReadOnly;
        }

        if (updated != attributes)
        {
            File.SetAttributes(filePath, updated);
        }
    }

    /// <summary>
    /// Computes the relative path from <paramref name="basePath"/> to <paramref name="fullPath"/>.
    /// </summary>
    /// <param name="basePath">The base directory the result is relative to.</param>
    /// <param name="fullPath">The target path.</param>
    /// <returns>The path of <paramref name="fullPath"/> relative to <paramref name="basePath"/>.</returns>
    public string GetRelativePath(string basePath, string fullPath)
    {
        return Path.GetRelativePath(basePath, fullPath);
    }

    /// <summary>
    /// Combines the supplied path segments into a single path.
    /// </summary>
    /// <param name="paths">The ordered path segments to join.</param>
    /// <returns>The combined path.</returns>
    public string CombinePath(params string[] paths)
    {
        return Path.Join(paths);
    }

    /// <summary>
    /// Returns the directory portion of the supplied path.
    /// </summary>
    /// <param name="filePath">The path whose directory name is requested.</param>
    /// <returns>The directory name, or <see langword="null"/> if the path denotes a root.</returns>
    public string? GetDirectoryName(string filePath)
    {
        return Path.GetDirectoryName(filePath);
    }

    /// <summary>
    /// Opens a file for asynchronous, sequential read access, sharing it with programs that keep it
    /// open for writing or deletion, as log writers and databases do.
    /// </summary>
    /// <param name="filePath">The path of the file to open.</param>
    /// <param name="bufferSize">The stream buffer size in bytes.</param>
    /// <returns>A readable stream over the file.</returns>
    public Stream OpenReadStream(string filePath, int bufferSize)
    {
        return new FileStream(
            filePath,
            new FileStreamOptions
            {
                Access = FileAccess.Read,
                Mode = FileMode.Open,
                Share = FileShare.ReadWrite | FileShare.Delete,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                BufferSize = bufferSize,
            }
        );
    }

    /// <summary>
    /// Creates a new file and opens it for asynchronous, sequential write access. Existing entries
    /// are never followed or truncated, which lets callers publish through an atomic rename without
    /// exposing an overwrite-through-symlink window.
    /// </summary>
    /// <param name="filePath">The path of the file to create.</param>
    /// <param name="bufferSize">The stream buffer size in bytes.</param>
    /// <returns>A writable stream over the file.</returns>
    private static FileStream CreateNewWriteStream(string filePath, int bufferSize)
    {
        return new FileStream(
            filePath,
            new FileStreamOptions
            {
                Access = FileAccess.Write,
                Mode = FileMode.CreateNew,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                BufferSize = bufferSize,
            }
        );
    }

    /// <summary>
    /// Writes a complete file through a create-new, randomly named sibling and atomically replaces
    /// the target only after the stream has closed successfully.
    /// </summary>
    /// <param name="finalPath">The final path to publish.</param>
    /// <param name="writer">The callback that writes the complete temporary file.</param>
    /// <param name="cancellationToken">A token to cancel the operation before publication.</param>
    /// <returns>A task that completes after the temporary file has been renamed into place.</returns>
    public async Task WriteFileAtomicallyAsync(
        string finalPath,
        Func<Stream, CancellationToken, Task> writer,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(finalPath);
        ArgumentNullException.ThrowIfNull(writer);

        var directory = Path.GetDirectoryName(finalPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException($"File path '{finalPath}' has no directory.");
        }

        var randomSuffix = RandomNumberGenerator.GetBytes(12);
        string tempPath;

        try
        {
            tempPath = Path.Join(
                directory,
                "." + Convert.ToHexStringLower(randomSuffix) + ".tmp"
            );
        }
        finally
        {
            CryptographicOperations.ZeroMemory(randomSuffix);
        }

        try
        {
            await using (
                var stream = CreateNewWriteStream(tempPath, StreamConstants.CopyBufferSize)
            )
            {
                await writer(stream, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            ClearReadOnlyAttribute(finalPath);
            File.Move(tempPath, finalPath, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(tempPath);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // The original write or rename failure is the actionable error.
            }

            throw;
        }
    }

    /// <summary>
    /// Computes the SHA-256 hash of a file's contents and returns it as a Base64 string.
    /// </summary>
    /// <param name="filePath">The path of the file to hash.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The Base64-encoded SHA-256 digest of the file.</returns>
    public async Task<string> ComputeFileHashAsync(
        string filePath,
        CancellationToken cancellationToken = default
    )
    {
        await using FileStream stream = new(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            StreamConstants.CopyBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan
        );

        var hash = await SHA256.HashDataAsync(stream, cancellationToken);

        try
        {
            return Convert.ToBase64String(hash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(hash);
        }
    }

    /// <summary>
    /// Reads a complete file into memory while enforcing a limit against the same opened stream, so
    /// a size-check/read race cannot trigger an unbounded allocation.
    /// </summary>
    /// <param name="filePath">The path of the file to read.</param>
    /// <param name="maximumBytes">The greatest file length the caller accepts.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The complete file contents.</returns>
    public async Task<byte[]> ReadAllBytesBoundedAsync(
        string filePath,
        int maximumBytes,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumBytes);

        await using FileStream stream = new(
            filePath,
            new FileStreamOptions
            {
                Access = FileAccess.Read,
                Mode = FileMode.Open,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                BufferSize = StreamConstants.CopyBufferSize,
            }
        );

        if (stream.Length > maximumBytes)
        {
            throw new InvalidDataException("File exceeds the permitted in-memory size.");
        }

        var bytes = new byte[(int)stream.Length];
        var extraByte = new byte[1];
        var totalRead = 0;

        try
        {
            while (totalRead < bytes.Length)
            {
                var read = await stream
                    .ReadAsync(bytes.AsMemory(totalRead), cancellationToken)
                    .ConfigureAwait(false);

                if (read is 0)
                {
                    break;
                }

                totalRead += read;
            }

            if (
                await stream
                    .ReadAsync(extraByte.AsMemory(), cancellationToken)
                    .ConfigureAwait(false)
                is not 0
            )
            {
                throw new InvalidDataException("File changed while it was being read.");
            }

            if (totalRead == bytes.Length)
            {
                return bytes;
            }

            var result = bytes[..totalRead];
            CryptographicOperations.ZeroMemory(bytes);
            return result;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(bytes);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(extraByte);
        }
    }

    /// <summary>
    /// Opens the lock file with no sharing at all and marks it for deletion once the handle closes,
    /// which the operating system also does when the process dies, so a crash never leaves a lock
    /// that blocks the backup forever.
    /// </summary>
    /// <param name="lockFilePath">The path of the lock file.</param>
    /// <returns>The open lock file, which releases the lock when disposed.</returns>
    public IDisposable AcquireExclusiveLock(string lockFilePath)
    {
        return new FileStream(
            lockFilePath,
            new FileStreamOptions
            {
                Mode = FileMode.OpenOrCreate,
                Access = FileAccess.ReadWrite,
                Share = FileShare.None,
                Options = FileOptions.DeleteOnClose,
            }
        );
    }

    /// <summary>
    /// Probes the lock file with the most permissive sharing: the open only fails while another
    /// process holds it exclusively. A missing file, a file left behind by a crash, or one that cannot
    /// be opened for any other reason is not treated as a held lock.
    /// </summary>
    /// <param name="lockFilePath">The path of the lock file.</param>
    /// <returns><see langword="true"/> when another process holds the lock.</returns>
    public bool IsLockHeld(string lockFilePath)
    {
        if (!File.Exists(lockFilePath))
        {
            return false;
        }

        try
        {
            using FileStream probe = new(
                lockFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete
            );

            return false;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Clears the read-only attribute of an existing file on Windows, where it would otherwise stop
    /// the file from being replaced, for example when a backup is restored again into the same folder.
    /// </summary>
    /// <param name="filePath">The file about to be replaced.</param>
    private static void ClearReadOnlyAttribute(string filePath)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(filePath))
        {
            return;
        }

        var attributes = File.GetAttributes(filePath);
        if (attributes.HasFlag(FileAttributes.ReadOnly))
        {
            File.SetAttributes(filePath, attributes & ~FileAttributes.ReadOnly);
        }
    }

    /// <summary>
    /// Walks the tree depth-first with an explicit stack, listing one folder at a time.
    /// </summary>
    /// <param name="root">The root directory to walk.</param>
    /// <param name="cancellationToken">A token to cancel the walk.</param>
    /// <returns>The files and folders found and the entries that were skipped.</returns>
    private static DirectoryScan ScanDirectory(string root, CancellationToken cancellationToken)
    {
        List<string> files = [];
        List<string> emptyDirectories = [];
        List<InaccessibleDirectory> inaccessibleDirectories = [];
        List<string> skippedLinks = [];
        Stack<string> pending = new();

        AddEntries(root, ListDirectory(root), isRoot: true);

        while (pending.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();

            List<ScannedEntry> entries;
            try
            {
                entries = ListDirectory(directory);
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                inaccessibleDirectories.Add(new InaccessibleDirectory(directory, exception));
                continue;
            }

            AddEntries(directory, entries, isRoot: false);
        }

        return new DirectoryScan(files, emptyDirectories, inaccessibleDirectories, skippedLinks);

        void AddEntries(string directory, List<ScannedEntry> entries, bool isRoot)
        {
            var keptEntries = 0;

            foreach (var entry in entries)
            {
                if (entry.IsLink)
                {
                    skippedLinks.Add(entry.FullPath);
                }
                else if (entry.IsDirectory)
                {
                    pending.Push(entry.FullPath);
                    keptEntries++;
                }
                else
                {
                    files.Add(entry.FullPath);
                    keptEntries++;
                }
            }

            if (!isRoot && keptEntries is 0)
            {
                emptyDirectories.Add(directory);
            }
        }
    }

    /// <summary>
    /// Lists the immediate entries of one folder, surfacing any failure to open or read it.
    /// </summary>
    /// <param name="directory">The folder to list.</param>
    /// <returns>The folder's entries.</returns>
    private static List<ScannedEntry> ListDirectory(string directory)
    {
        FileSystemEnumerable<ScannedEntry> enumerable = new(
            directory,
            static (ref entry) =>
            {
                var fullPath = entry.ToFullPath();
                var isLink =
                    entry.Attributes.HasFlag(FileAttributes.ReparsePoint) && IsLink(fullPath, entry.IsDirectory);

                return new ScannedEntry(fullPath, entry.IsDirectory, isLink);
            },
            new EnumerationOptions
            {
                RecurseSubdirectories = false,
                IgnoreInaccessible = false,
                AttributesToSkip = FileAttributes.None,
                ReturnSpecialDirectories = false,
            }
        );

        return [.. enumerable];
    }

    /// <summary>
    /// Determines whether a reparse point is a symbolic link or junction rather than a file whose data
    /// merely lives behind a filter, such as a cloud placeholder.
    /// </summary>
    /// <param name="fullPath">The full path of the reparse point.</param>
    /// <param name="isDirectory">Whether the entry is a folder.</param>
    /// <returns><see langword="true"/> when the entry links to another location.</returns>
    private static bool IsLink(string fullPath, bool isDirectory)
    {
        try
        {
            FileSystemInfo info = isDirectory ? new DirectoryInfo(fullPath) : new FileInfo(fullPath);
            return info.LinkTarget is not null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>
    /// One entry found while walking a folder.
    /// </summary>
    /// <param name="FullPath">The entry's full path.</param>
    /// <param name="IsDirectory">Whether the entry is a folder.</param>
    /// <param name="IsLink">Whether the entry is a symbolic link or junction.</param>
    private readonly record struct ScannedEntry(string FullPath, bool IsDirectory, bool IsLink);
}
