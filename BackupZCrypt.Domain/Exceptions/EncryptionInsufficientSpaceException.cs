namespace BackupZCrypt.Domain.Exceptions;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Resources;

public class EncryptionInsufficientSpaceException(string path)
    : EncryptionException(
        EncryptionErrorCode.InsufficientDiskSpace,
        message: string.Format(Messages.InsufficientDiskSpaceFormat, path))
{
}
