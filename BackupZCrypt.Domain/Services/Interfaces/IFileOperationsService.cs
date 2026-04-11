namespace BackupZCrypt.Domain.Services.Interfaces;

public interface IFileOperationsService
{
    Task<string[]> GetFilesAsync(
        string directoryPath,
        string searchPattern = "*.*",
        CancellationToken cancellationToken = default);

    bool DirectoryExists(string directoryPath);

    bool FileExists(string filePath);

    void CreateDirectory(string directoryPath);

    Task CreateDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default);

    void DeleteFile(string filePath);

    Task DeleteDirectoryAsync(
        string directoryPath,
        bool recursive,
        CancellationToken cancellationToken = default);

    long GetFileSize(string filePath);

    string GetFullPath(string filePath);

    string GetRelativePath(string basePath, string fullPath);

    string CombinePath(params string[] paths);

    string? GetDirectoryName(string filePath);

    Stream OpenReadStream(string filePath, int bufferSize);

    Stream CreateWriteStream(string filePath, int bufferSize);

    Stream CreateTempStream(int bufferSize);

    Task<string> ComputeFileHashAsync(string filePath, CancellationToken cancellationToken = default);
}
