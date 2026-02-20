namespace CloudZCrypt.Domain.Exceptions;

using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Resources;

public class EncryptionCipherException(string operation, Exception innerException)
    : EncryptionException(
        EncryptionErrorCode.CipherOperationFailed,
        message: string.Format(Messages.CipherOperationFailedFormat, operation),
        innerException)
{
}
