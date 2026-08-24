namespace BackupZCrypt.Domain.Services.Interfaces;

/// <summary>
/// Abstracts file-system access used by backup and restore operations so the
/// lower layers do not depend directly on platform I/O APIs.
/// </summary>
public interface IFileOperationsService
{
    /// <summary>
    /// Recursively enumerates files under a directory that match a search pattern, skipping
    /// inaccessible entries and excluding all reparse points (symbolic links or junctions), so
    /// enumeration cannot cycle, escape the source tree, or copy a linked file target.
    /// </summary>
    /// <param name="directoryPath">The root directory to search.</param>
    /// <param name="searchPattern">The wildcard expression matched against file names; defaults to all files.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The full paths of the matching files.</returns>
    public Task<string[]> GetFilesAsync(
        string directoryPath,
        string searchPattern = "*",
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Determines whether the specified directory exists.
    /// </summary>
    /// <param name="directoryPath">The directory path to test.</param>
    /// <returns><see langword="true"/> if the directory exists; otherwise <see langword="false"/>.</returns>
    public bool DirectoryExists(string directoryPath);

    /// <summary>
    /// Determines whether the specified file exists.
    /// </summary>
    /// <param name="filePath">The file path to test.</param>
    /// <returns><see langword="true"/> if the file exists; otherwise <see langword="false"/>.</returns>
    public bool FileExists(string filePath);

    /// <summary>
    /// Determines whether an existing file-system entry is a symbolic link, junction, or other
    /// reparse point.
    /// </summary>
    /// <param name="path">The existing entry to inspect.</param>
    /// <returns><see langword="true"/> when the entry is a reparse point.</returns>
    public bool IsReparsePoint(string path);

    /// <summary>
    /// Creates the specified directory, including any missing parent directories.
    /// </summary>
    /// <param name="directoryPath">The directory path to create.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the directory has been created.</returns>
    public Task CreateDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the specified file.
    /// </summary>
    /// <param name="filePath">The path of the file to delete.</param>
    public void DeleteFile(string filePath);

    /// <summary>
    /// Removes all files and subdirectories from a directory while keeping the directory itself.
    /// </summary>
    /// <param name="directoryPath">The directory to clean.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the directory has been emptied.</returns>
    public Task CleanDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the size, in bytes, of the specified file.
    /// </summary>
    /// <param name="filePath">The path of the file to measure.</param>
    /// <returns>The file size in bytes.</returns>
    public long GetFileSize(string filePath);

    /// <summary>
    /// Computes the path of a target relative to a base path.
    /// </summary>
    /// <param name="basePath">The base path the result is relative to.</param>
    /// <param name="fullPath">The target path to express relatively.</param>
    /// <returns>The relative path from <paramref name="basePath"/> to <paramref name="fullPath"/>.</returns>
    public string GetRelativePath(string basePath, string fullPath);

    /// <summary>
    /// Combines path segments into a single path.
    /// </summary>
    /// <param name="paths">The path segments to combine.</param>
    /// <returns>The combined path.</returns>
    public string CombinePath(params string[] paths);

    /// <summary>
    /// Gets the directory portion of the specified path.
    /// </summary>
    /// <param name="filePath">The path whose directory is requested.</param>
    /// <returns>The directory name, or <see langword="null"/> if the path has none.</returns>
    public string? GetDirectoryName(string filePath);

    /// <summary>
    /// Opens a file for reading.
    /// </summary>
    /// <param name="filePath">The path of the file to open.</param>
    /// <param name="bufferSize">The buffer size, in bytes, to use for the stream.</param>
    /// <returns>A readable stream over the file.</returns>
    public Stream OpenReadStream(string filePath, int bufferSize);

    /// <summary>
    /// Writes a complete file through a randomly named sibling and atomically replaces the target
    /// only after the write succeeds.
    /// </summary>
    /// <param name="finalPath">The final path to publish.</param>
    /// <param name="writer">The callback that writes the complete temporary file.</param>
    /// <param name="cancellationToken">A token to cancel the operation before publication.</param>
    /// <returns>A task that completes after the temporary file has been renamed into place.</returns>
    public Task WriteFileAtomicallyAsync(
        string finalPath,
        Func<Stream, CancellationToken, Task> writer,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Computes the SHA-256 hash of the specified file's contents.
    /// </summary>
    /// <param name="filePath">The path of the file to hash.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The Base64-encoded SHA-256 digest of the file.</returns>
    public Task<string> ComputeFileHashAsync(
        string filePath,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Reads a complete file into memory while enforcing an allocation limit on the opened stream.
    /// </summary>
    /// <param name="filePath">The path of the file to read.</param>
    /// <param name="maximumBytes">The greatest file length the caller accepts.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The complete file contents.</returns>
    public Task<byte[]> ReadAllBytesBoundedAsync(
        string filePath,
        int maximumBytes,
        CancellationToken cancellationToken = default
    );
}
