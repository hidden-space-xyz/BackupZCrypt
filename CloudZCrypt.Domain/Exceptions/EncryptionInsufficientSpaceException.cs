namespace CloudZCrypt.Domain.Exceptions;

using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Resources;

public class EncryptionInsufficientSpaceException(string path)
    : EncryptionException(
        EncryptionErrorCode.InsufficientDiskSpace,
        message: string.Format(Messages.InsufficientDiskSpaceFormat, path))
{
}
