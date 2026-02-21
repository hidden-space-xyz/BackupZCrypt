namespace BackupZCrypt.Domain.Exceptions;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Resources;

public class EncryptionAccessDeniedException(string filePath, Exception innerException)
    : EncryptionException(
        EncryptionErrorCode.AccessDenied,
        message: string.Format(Messages.AccessDeniedFormat, filePath),
        innerException)
{
}
