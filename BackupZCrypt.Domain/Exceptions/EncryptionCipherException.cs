namespace BackupZCrypt.Domain.Exceptions;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Resources;

public class EncryptionCipherException(string operation, Exception innerException)
    : EncryptionException(
        EncryptionErrorCode.CipherOperationFailed,
        message: string.Format(Messages.CipherOperationFailedFormat, operation),
        innerException)
{
}
