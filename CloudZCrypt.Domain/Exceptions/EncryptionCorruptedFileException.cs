namespace CloudZCrypt.Domain.Exceptions;

using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Resources;

public class EncryptionCorruptedFileException(string filePath)
    : EncryptionException(
        EncryptionErrorCode.FileCorruption,
        message: string.Format(Messages.CorruptedFileFormat, filePath))
{
}
