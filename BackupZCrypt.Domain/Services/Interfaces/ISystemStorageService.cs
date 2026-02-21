namespace BackupZCrypt.Domain.Services.Interfaces;

public interface ISystemStorageService
{
    string? GetPathRoot(string fullPath);

    long GetAvailableFreeSpace(string rootPath);

    bool IsDriveReady(string rootPath);
}
