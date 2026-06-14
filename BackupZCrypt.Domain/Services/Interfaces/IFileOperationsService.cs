namespace BackupZCrypt.Domain.Services.Interfaces;

/// <summary>
/// Abstracts file-system access used by backup and restore operations so the
/// lower layers do not depend directly on platform I/O APIs.
/// </summary>
public interface IFileOperationsService
{
    /// <summary>
    /// Enumerates files within a directory that match a search pattern.
    /// </summary>
    /// <param name="directoryPath">The directory to search.</param>
    /// <param name="searchPattern">The search pattern to match file names against.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
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
    /// Creates the specified directory, including any missing parent directories.
    /// </summary>
    /// <param name="directoryPath">The directory path to create.</param>
    public void CreateDirectory(string directoryPath);

    /// <summary>
    /// Asynchronously creates the specified directory, including any missing parent directories.
    /// </summary>
    /// <param name="directoryPath">The directory path to create.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task that completes when the directory has been created.</returns>
    public Task CreateDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the specified file.
    /// </summary>
    /// <param name="filePath">The path of the file to delete.</param>
    public void DeleteFile(string filePath);

    /// <summary>
    /// Moves a file to a new location, optionally overwriting an existing target.
    /// </summary>
    /// <param name="sourcePath">The path of the file to move.</param>
    /// <param name="destinationPath">The destination path.</param>
    /// <param name="overwrite">Whether to overwrite an existing file at the destination.</param>
    public void MoveFile(string sourcePath, string destinationPath, bool overwrite);

    /// <summary>
    /// Asynchronously deletes a directory, optionally including its contents.
    /// </summary>
    /// <param name="directoryPath">The directory to delete.</param>
    /// <param name="recursive">Whether to delete subdirectories and files within the directory.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task that completes when the directory has been deleted.</returns>
    public Task DeleteDirectoryAsync(
        string directoryPath,
        bool recursive,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Asynchronously removes all files and subdirectories from a directory while keeping the directory itself.
    /// </summary>
    /// <param name="directoryPath">The directory to clean.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task that completes when the directory has been emptied.</returns>
    public Task CleanDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the size, in bytes, of the specified file.
    /// </summary>
    /// <param name="filePath">The path of the file to measure.</param>
    /// <returns>The file size in bytes.</returns>
    public long GetFileSize(string filePath);

    /// <summary>
    /// Resolves the specified path to an absolute path.
    /// </summary>
    /// <param name="filePath">The path to resolve.</param>
    /// <returns>The fully qualified absolute path.</returns>
    public string GetFullPath(string filePath);

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
    /// Creates or truncates a file and opens it for writing.
    /// </summary>
    /// <param name="filePath">The path of the file to create.</param>
    /// <param name="bufferSize">The buffer size, in bytes, to use for the stream.</param>
    /// <returns>A writable stream over the file.</returns>
    public Stream CreateWriteStream(string filePath, int bufferSize);

    /// <summary>
    /// Computes a content hash for the specified file.
    /// </summary>
    /// <param name="filePath">The path of the file to hash.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The computed hash encoded as a string.</returns>
    public Task<string> ComputeFileHashAsync(
        string filePath,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Reads the entire contents of a file into a byte array.
    /// </summary>
    /// <param name="filePath">The path of the file to read.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The file contents as a byte array.</returns>
    public Task<byte[]> ReadAllBytesAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a byte array to a file, creating or overwriting it.
    /// </summary>
    /// <param name="filePath">The path of the file to write.</param>
    /// <param name="bytes">The bytes to write.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task that completes when the file has been written.</returns>
    public Task WriteAllBytesAsync(
        string filePath,
        byte[] bytes,
        CancellationToken cancellationToken = default
    );
}
