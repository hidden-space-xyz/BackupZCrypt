namespace BackupZCrypt.Domain.Services.Interfaces;

public interface IEncryptionFileService
{
    Stream OpenSourceFile(string sourceFilePath, bool validateHeader = false);

    Stream CreateWriteStream(string destinationFilePath);

    Stream CreateTempStream();

    void EnsureDirectoryExists(string filePath);

    void ValidateDiskSpace(string sourceFilePath, string destinationFilePath);

    void TryDeleteFile(string filePath);
}
