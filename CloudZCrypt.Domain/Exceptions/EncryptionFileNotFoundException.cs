namespace CloudZCrypt.Domain.Exceptions;

using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Resources;

public class EncryptionFileNotFoundException(string filePath)
    : EncryptionException(
        EncryptionErrorCode.FileNotFound,
        message: string.Format(Messages.FileNotFoundFormat, filePath))
{
}
