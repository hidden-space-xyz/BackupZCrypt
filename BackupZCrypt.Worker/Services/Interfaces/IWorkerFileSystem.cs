namespace BackupZCrypt.Worker.Services.Interfaces;

internal interface IWorkerFileSystem
{
    bool HasFilesToBackup(string path);

    bool HasFilesToRestore(string path);

    bool IsManifestEncrypted(string sourcePath);

    void DeleteDirectoryContents(string path);
}
