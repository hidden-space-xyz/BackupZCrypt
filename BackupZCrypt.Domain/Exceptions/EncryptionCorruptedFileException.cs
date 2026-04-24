namespace BackupZCrypt.Domain.Exceptions;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Resources;

public class EncryptionCorruptedFileException : EncryptionException
{
    public EncryptionCorruptedFileException()
        : base(EncryptionErrorCode.FileCorruption)
    {
    }

    public EncryptionCorruptedFileException(string? message)
        : base(EncryptionErrorCode.FileCorruption, message)
    {
    }

    public EncryptionCorruptedFileException(string? message, Exception innerException)
        : base(EncryptionErrorCode.FileCorruption, message, innerException)
    {
    }

    public static EncryptionCorruptedFileException CreateForFilePath(string filePath) =>
        new(string.Format(Messages.CorruptedFileFormat, filePath));

    protected EncryptionCorruptedFileException(
        EncryptionErrorCode code,
        string? message = null)
        : base(code, message)
    {
    }

    protected EncryptionCorruptedFileException(
        EncryptionErrorCode code,
        string? message,
        Exception innerException)
        : base(code, message, innerException)
    {
    }
}
