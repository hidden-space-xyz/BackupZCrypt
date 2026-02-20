namespace CloudZCrypt.Infrastructure.Services.FileSystem;

using CloudZCrypt.Domain.Services.Interfaces;

internal class FileOperationsService : IFileOperationsService
{
    public async Task<string[]> GetFilesAsync(
        string directoryPath,
        string searchPattern = "*.*",
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(
            () => Directory.GetFiles(directoryPath, searchPattern, SearchOption.AllDirectories),
            cancellationToken);
    }

    public bool DirectoryExists(string directoryPath)
    {
        return Directory.Exists(directoryPath);
    }

    public bool FileExists(string filePath)
    {
        return File.Exists(filePath);
    }

    public void CreateDirectory(string directoryPath)
    {
        Directory.CreateDirectory(directoryPath);
    }

    public async Task CreateDirectoryAsync(
        string directoryPath,
        CancellationToken cancellationToken = default)
    {
        await Task.Run(() => Directory.CreateDirectory(directoryPath), cancellationToken);
    }

    public void DeleteFile(string filePath)
    {
        File.Delete(filePath);
    }

    public long GetFileSize(string filePath)
    {
        return new FileInfo(filePath).Length;
    }

    public string GetFullPath(string filePath)
    {
        return Path.GetFullPath(filePath);
    }

    public string GetRelativePath(string basePath, string fullPath)
    {
        return Path.GetRelativePath(basePath, fullPath);
    }

    public string CombinePath(params string[] paths)
    {
        return Path.Combine(paths);
    }

    public string? GetDirectoryName(string filePath)
    {
        return Path.GetDirectoryName(filePath);
    }

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
            });
    }

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
            });
    }

    public Stream CreateTempStream(int bufferSize)
    {
        string tempFilePath = Path.GetTempFileName();
        return new FileStream(
            tempFilePath,
            new FileStreamOptions
            {
                Access = FileAccess.ReadWrite,
                Mode = FileMode.Create,
                Options =
                    FileOptions.Asynchronous
                    | FileOptions.SequentialScan
                    | FileOptions.DeleteOnClose,
                BufferSize = bufferSize,
            });
    }
}
