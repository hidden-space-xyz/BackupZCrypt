using System.IO.Enumeration;
using System.Security.Cryptography;

using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Services.Interfaces;

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
    public void DeleteFile(string filePath)
    {
        File.Delete(filePath);
    }

    /// <summary>
    /// Removes all files and subdirectories from the specified directory while leaving the
    /// directory itself in place. Runs on a background thread.
    /// </summary>
    /// <param name="directoryPath">The directory whose contents are removed.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the directory has been cleaned.</returns>
    public async Task CleanDirectoryAsync(
        string directoryPath,
        CancellationToken cancellationToken = default
    )
    {
        await Task.Run(
            () =>
            {
                var directory = new DirectoryInfo(directoryPath);

                foreach (var file in directory.GetFiles())
                {
                    file.Delete();
                }

                foreach (var subDirectory in directory.GetDirectories())
                {
                    subDirectory.Delete(recursive: true);
                }
            },
            cancellationToken
        );
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
        return Path.Combine(paths);
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
    /// Opens a file for asynchronous, sequential read access.
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
            tempPath = Path.Combine(
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
            FileShare.Read,
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
}
