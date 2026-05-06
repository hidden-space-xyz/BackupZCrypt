namespace BackupZCrypt.Infrastructure.Services.Encryption;

using BackupZCrypt.Domain.Resources;
using BackupZCrypt.Domain.Services.Interfaces;
using BackupZCrypt.Infrastructure.Constants;

internal sealed class EncryptionFileService(
    IFileOperationsService fileOperationsService,
    ISystemStorageService systemStorageService) : IEncryptionFileService
{
    public Stream OpenSourceFile(string sourceFilePath, bool validateHeader = false)
    {
        if (!fileOperationsService.FileExists(sourceFilePath))
        {
            throw new FileNotFoundException(
                string.Format(Messages.FileNotFoundFormat, sourceFilePath), sourceFilePath);
        }

        var stream = fileOperationsService.OpenReadStream(
            sourceFilePath,
            EncryptionConstants.BufferSize);

        if (validateHeader && stream.Length < EncryptionConstants.HeaderSize)
        {
            stream.Dispose();
            throw new InvalidDataException(
                string.Format(Messages.CorruptedFileFormat, sourceFilePath));
        }

        return stream;
    }

    public Stream CreateWriteStream(string destinationFilePath)
    {
        return fileOperationsService.CreateWriteStream(
            destinationFilePath,
            EncryptionConstants.BufferSize);
    }

    public Stream CreateTempStream()
    {
        return fileOperationsService.CreateTempStream(EncryptionConstants.BufferSize);
    }

    public void EnsureDirectoryExists(string filePath)
    {
        var directory = fileOperationsService.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            fileOperationsService.CreateDirectory(directory);
        }
    }

    public void ValidateDiskSpace(string sourceFilePath, string destinationFilePath)
    {
        try
        {
            var sourceLength = fileOperationsService.GetFileSize(sourceFilePath);
            var fullPath = fileOperationsService.GetFullPath(destinationFilePath);
            var destinationDrive = systemStorageService.GetPathRoot(fullPath);

            if (!string.IsNullOrEmpty(destinationDrive))
            {
                var availableSpace = systemStorageService.GetAvailableFreeSpace(destinationDrive);

                if (availableSpace >= 0)
                {
                    var requiredSpace = (long)(sourceLength * 1.2) + 1024;

                    if (availableSpace < requiredSpace)
                    {
                        throw new IOException(
                            string.Format(Messages.InsufficientDiskSpaceFormat, destinationFilePath));
                    }
                }
            }
        }
        catch (IOException)
        {
            throw;
        }
        catch
        {
            // If we can't check disk space, continue anyway
        }
    }

    public void TryDeleteFile(string filePath)
    {
        try
        {
            if (fileOperationsService.FileExists(filePath))
            {
                fileOperationsService.DeleteFile(filePath);
            }
        }
        catch
        { /* ignore */
        }
    }
}
