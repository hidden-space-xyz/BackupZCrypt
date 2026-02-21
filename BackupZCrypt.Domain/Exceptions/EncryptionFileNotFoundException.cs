namespace BackupZCrypt.Domain.Exceptions;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Resources;

public class EncryptionFileNotFoundException(string filePath)
    : EncryptionException(
        EncryptionErrorCode.FileNotFound,
        message: string.Format(Messages.FileNotFoundFormat, filePath))
{
}
