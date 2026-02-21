namespace BackupZCrypt.Infrastructure.Services.Encryption;

using BackupZCrypt.Domain.Exceptions;
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
            throw new EncryptionFileNotFoundException(sourceFilePath);
        }

        Stream stream;
        try
        {
            stream = fileOperationsService.OpenReadStream(
                sourceFilePath,
                EncryptionConstants.BufferSize);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new EncryptionAccessDeniedException(sourceFilePath, ex);
        }

        if (validateHeader && stream.Length < EncryptionConstants.HeaderSize)
        {
            stream.Dispose();
            throw new EncryptionCorruptedFileException(sourceFilePath);
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
        string? directory = fileOperationsService.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            try
            {
                fileOperationsService.CreateDirectory(directory);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new EncryptionAccessDeniedException(filePath, ex);
            }
        }
    }

    public void ValidateDiskSpace(string sourceFilePath, string destinationFilePath)
    {
        try
        {
            long sourceLength = fileOperationsService.GetFileSize(sourceFilePath);
            string fullPath = fileOperationsService.GetFullPath(destinationFilePath);
            string? destinationDrive = systemStorageService.GetPathRoot(fullPath);

            if (!string.IsNullOrEmpty(destinationDrive))
            {
                long availableSpace = systemStorageService.GetAvailableFreeSpace(destinationDrive);

                if (availableSpace >= 0)
                {
                    long requiredSpace = (long)(sourceLength * 1.2) + 1024;

                    if (availableSpace < requiredSpace)
                    {
                        throw new EncryptionInsufficientSpaceException(destinationFilePath);
                    }
                }
            }
        }
        catch (EncryptionException)
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
