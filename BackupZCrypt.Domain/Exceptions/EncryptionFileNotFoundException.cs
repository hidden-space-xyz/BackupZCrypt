namespace BackupZCrypt.Domain.Exceptions;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Resources;

public class EncryptionFileNotFoundException : EncryptionException
{
    public EncryptionFileNotFoundException()
        : base(EncryptionErrorCode.FileNotFound)
    {
    }

    public EncryptionFileNotFoundException(string? message)
        : base(EncryptionErrorCode.FileNotFound, message)
    {
    }

    public EncryptionFileNotFoundException(string? message, Exception innerException)
        : base(EncryptionErrorCode.FileNotFound, message, innerException)
    {
    }

    public static EncryptionFileNotFoundException CreateForFilePath(string filePath) =>
        new(string.Format(Messages.FileNotFoundFormat, filePath));

    protected EncryptionFileNotFoundException(
        EncryptionErrorCode code,
        string? message = null)
        : base(code, message)
    {
    }

    protected EncryptionFileNotFoundException(
        EncryptionErrorCode code,
        string? message,
        Exception innerException)
        : base(code, message, innerException)
    {
    }
}
