namespace BackupZCrypt.Domain.Exceptions;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Resources;

public class FileNotFoundException : EncryptionException
{
    public FileNotFoundException()
        : base(BackupErrorCode.FileNotFound)
    {
    }

    public FileNotFoundException(string? message)
        : base(BackupErrorCode.FileNotFound, message)
    {
    }

    public FileNotFoundException(string? message, Exception innerException)
        : base(BackupErrorCode.FileNotFound, message, innerException)
    {
    }

    public static FileNotFoundException CreateForFilePath(string filePath) =>
        new(string.Format(Messages.FileNotFoundFormat, filePath));

    protected FileNotFoundException(
        BackupErrorCode code,
        string? message = null)
        : base(code, message)
    {
    }

    protected FileNotFoundException(
        BackupErrorCode code,
        string? message,
        Exception innerException)
        : base(code, message, innerException)
    {
    }
}
