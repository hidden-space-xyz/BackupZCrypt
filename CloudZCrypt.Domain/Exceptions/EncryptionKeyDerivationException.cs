namespace CloudZCrypt.Domain.Exceptions;

using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Resources;

public class EncryptionKeyDerivationException(Exception innerException)
    : EncryptionException(
        EncryptionErrorCode.KeyDerivationFailed,
        message: Messages.KeyDerivationFailed,
        innerException)
{
}
