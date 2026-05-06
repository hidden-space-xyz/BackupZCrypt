namespace BackupZCrypt.Domain.Exceptions;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Resources;

public class CorruptedFileException : EncryptionException
{
    public CorruptedFileException()
        : base(BackupErrorCode.FileCorruption)
    {
    }

    public CorruptedFileException(string? message)
        : base(BackupErrorCode.FileCorruption, message)
    {
    }

    public CorruptedFileException(string? message, Exception innerException)
        : base(BackupErrorCode.FileCorruption, message, innerException)
    {
    }

    public static CorruptedFileException CreateForFilePath(string filePath) =>
        new(string.Format(Messages.CorruptedFileFormat, filePath));

    protected CorruptedFileException(
        BackupErrorCode code,
        string? message = null)
        : base(code, message)
    {
    }

    protected CorruptedFileException(
        BackupErrorCode code,
        string? message,
        Exception innerException)
        : base(code, message, innerException)
    {
    }
}
