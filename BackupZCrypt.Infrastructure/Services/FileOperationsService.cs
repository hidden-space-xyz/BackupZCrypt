using System.IO.Enumeration;
using System.Security.Cryptography;

using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Services.Interfaces;

namespace BackupZCrypt.Infrastructure.Services;

/// <summary>
/// File-system implementation of <see cref="IFileOperationsService"/> backed by
/// <see cref="System.IO"/>. Stream factory methods open files asynchronously with
/// sequential-scan hints, and hashing/reads use the shared <see cref="StreamConstants"/>
/// buffer sizes.
/// </summary>
internal sealed class FileOperationsService : IFileOperationsService
{
    /// <summary>
    /// Recursively enumerates files under <paramref name="directoryPath"/> that match
    /// <paramref name="searchPattern"/>, skipping inaccessible entries. Directory reparse
    /// points (symlinks/junctions) are never followed, preventing cycles or inclusion of
    /// content outside the source tree.
    /// </summary>
    /// <param name="directoryPath">The root directory to enumerate.</param>
    /// <param name="searchPattern">A simple wildcard expression matched against file names; defaults to all files.</param>
    /// <param name="cancellationToken">Token used to cancel the enumeration.</param>
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
                        && FileSystemName.MatchesSimpleExpression(searchPattern, entry.FileName),
                    ShouldRecursePredicate = static (ref entry) =>
                        (entry.Attributes & FileAttributes.ReparsePoint) == 0,
                };

                return enumerable.ToArray();
            },
            cancellationToken
        );
    }

    /// <summary>
    /// Determines whether the specified directory exists.
    /// </summary>
    /// <param name="directoryPath">The directory path to test.</param>
    /// <returns><see langword="true"/> if the directory exists; otherwise <see langword="false"/>.</returns>
    public bool DirectoryExists(string directoryPath)
    {
        return Directory.Exists(directoryPath);
    }

    /// <summary>
    /// Determines whether the specified file exists.
    /// </summary>
    /// <param name="filePath">The file path to test.</param>
    /// <returns><see langword="true"/> if the file exists; otherwise <see langword="false"/>.</returns>
    public bool FileExists(string filePath)
    {
        return File.Exists(filePath);
    }

    /// <summary>
    /// Creates the specified directory and any missing parent directories.
    /// </summary>
    /// <param name="directoryPath">The directory path to create.</param>
    public void CreateDirectory(string directoryPath)
    {
        _ = Directory.CreateDirectory(directoryPath);
    }

    /// <summary>
    /// Creates the specified directory (and any missing parents) on a background thread.
    /// </summary>
    /// <param name="directoryPath">The directory path to create.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task that completes when the directory has been created.</returns>
    public async Task CreateDirectoryAsync(
        string directoryPath,
        CancellationToken cancellationToken = default
    )
    {
        _ = await Task.Run(() => Directory.CreateDirectory(directoryPath), cancellationToken);
    }

    /// <summary>
    /// Deletes the specified file.
    /// </summary>
    /// <param name="filePath">The path of the file to delete.</param>
    public void DeleteFile(string filePath)
    {
        File.Delete(filePath);
    }

    /// <summary>
    /// Moves a file to a new location, optionally overwriting an existing destination.
    /// </summary>
    /// <param name="sourcePath">The current path of the file.</param>
    /// <param name="destinationPath">The target path to move the file to.</param>
    /// <param name="overwrite"><see langword="true"/> to overwrite an existing destination file.</param>
    public void MoveFile(string sourcePath, string destinationPath, bool overwrite)
    {
        File.Move(sourcePath, destinationPath, overwrite);
    }

    /// <summary>
    /// Deletes the specified directory on a background thread.
    /// </summary>
    /// <param name="directoryPath">The directory path to delete.</param>
    /// <param name="recursive"><see langword="true"/> to delete the directory and all its contents.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task that completes when the directory has been deleted.</returns>
    public async Task DeleteDirectoryAsync(
        string directoryPath,
        bool recursive,
        CancellationToken cancellationToken = default
    )
    {
        await Task.Run(() => Directory.Delete(directoryPath, recursive), cancellationToken);
    }

    /// <summary>
    /// Removes all files and subdirectories from the specified directory while leaving the
    /// directory itself in place. Runs on a background thread.
    /// </summary>
    /// <param name="directoryPath">The directory whose contents are removed.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
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
    /// Resolves the supplied path to an absolute, fully qualified path.
    /// </summary>
    /// <param name="filePath">The path to resolve.</param>
    /// <returns>The absolute path.</returns>
    public string GetFullPath(string filePath)
    {
        return Path.GetFullPath(filePath);
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
    /// Creates (or truncates) a file and opens it for asynchronous, sequential write access.
    /// </summary>
    /// <param name="filePath">The path of the file to create.</param>
    /// <param name="bufferSize">The stream buffer size in bytes.</param>
    /// <returns>A writable stream over the file.</returns>
    public Stream CreateWriteStream(string filePath, int bufferSize)
    {
        return new FileStream(
            filePath,
            new FileStreamOptions
            {
                Access = FileAccess.Write,
                Mode = FileMode.Create,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                BufferSize = bufferSize,
            }
        );
    }

    /// <summary>
    /// Computes the SHA-256 hash of a file's contents and returns it as a Base64 string.
    /// </summary>
    /// <param name="filePath">The path of the file to hash.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
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
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Reads the entire contents of a file into a byte array.
    /// </summary>
    /// <param name="filePath">The path of the file to read.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The file's contents.</returns>
    public async Task<byte[]> ReadAllBytesAsync(
        string filePath,
        CancellationToken cancellationToken = default
    )
    {
        return await File.ReadAllBytesAsync(filePath, cancellationToken);
    }

    /// <summary>
    /// Writes the supplied bytes to a file, creating or overwriting it.
    /// </summary>
    /// <param name="filePath">The path of the file to write.</param>
    /// <param name="bytes">The contents to write.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task that completes when the bytes have been written.</returns>
    public async Task WriteAllBytesAsync(
        string filePath,
        byte[] bytes,
        CancellationToken cancellationToken = default
    )
    {
        await File.WriteAllBytesAsync(filePath, bytes, cancellationToken);
    }
}
