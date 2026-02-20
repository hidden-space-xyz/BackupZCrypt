namespace CloudZCrypt.Domain.Exceptions;

using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Resources;

public class EncryptionAccessDeniedException(string filePath, Exception innerException)
    : EncryptionException(
        EncryptionErrorCode.AccessDenied,
        message: string.Format(Messages.AccessDeniedFormat, filePath),
        innerException)
{
}
