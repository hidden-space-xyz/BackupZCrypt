using BackupZCrypt.Domain.Constants;

namespace BackupZCrypt.Worker.Services;

internal sealed partial class WorkerFileSystem(
    ILogger<WorkerFileSystem> logger) : IWorkerFileSystem
{
    public bool HasFilesToBackup(string path)
    {
        return Directory.Exists(path)
            && Directory.EnumerateFileSystemEntries(path).Any();
    }

    public bool HasFilesToRestore(string path)
    {
        if (!Directory.Exists(path))
        {
            return false;
        }

        var manifestPath = Path.Combine(path, BackupConstants.ManifestFileName);
        return File.Exists(manifestPath);
    }

    public bool IsManifestEncrypted(string sourcePath)
    {
        var manifestPath = Path.Combine(sourcePath, BackupConstants.ManifestFileName);

        if (!File.Exists(manifestPath))
        {
            return false;
        }

        try
        {
            using FileStream fs = new(
                manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var firstByte = fs.ReadByte();
            return firstByte >= 0 && firstByte != '{';
        }
        catch (IOException ex)
        {
            LogManifestReadFailed(ex, manifestPath);
            return false;
        }
    }

    public void DeleteDirectoryContents(string path)
    {
        try
        {
            var dir = new DirectoryInfo(path);

            foreach (var file in dir.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                file.Delete();
            }

            foreach (var subDir in dir.EnumerateDirectories())
            {
                subDir.Delete(recursive: true);
            }

            LogDeletedSourceFiles(path);
        }
        catch (IOException ex)
        {
            LogDeleteSourceFilesFailed(ex, path);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Failed to read manifest file '{Path}'.")]
    private partial void LogManifestReadFailed(Exception ex, string path);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Deleted source files in '{Path}'.")]
    private partial void LogDeletedSourceFiles(string path);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Failed to delete source files in '{Path}'.")]
    private partial void LogDeleteSourceFilesFailed(Exception ex, string path);
}
