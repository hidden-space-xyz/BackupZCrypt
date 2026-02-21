namespace BackupZCrypt.Domain.Exceptions;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Resources;

public class EncryptionCorruptedFileException(string filePath)
    : EncryptionException(
        EncryptionErrorCode.FileCorruption,
        message: string.Format(Messages.CorruptedFileFormat, filePath))
{
}
