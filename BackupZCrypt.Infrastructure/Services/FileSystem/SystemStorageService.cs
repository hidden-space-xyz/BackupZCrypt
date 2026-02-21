namespace BackupZCrypt.Infrastructure.Services.FileSystem;

using BackupZCrypt.Domain.Services.Interfaces;

internal class SystemStorageService : ISystemStorageService
{
    public string? GetPathRoot(string fullPath)
    {
        try
        {
            return Path.GetPathRoot(fullPath);
        }
        catch
        {
            return null;
        }
    }

    public long GetAvailableFreeSpace(string rootPath)
    {
        try
        {
            DriveInfo driveInfo = new(rootPath);
            return driveInfo.IsReady ? driveInfo.AvailableFreeSpace : -1;
        }
        catch
        {
            return -1;
        }
    }

    public bool IsDriveReady(string rootPath)
    {
        try
        {
            DriveInfo driveInfo = new(rootPath);
            return driveInfo.IsReady;
        }
        catch
        {
            return false;
        }
    }
}
