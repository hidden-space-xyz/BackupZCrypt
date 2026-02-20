namespace CloudZCrypt.Domain.Exceptions;

using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Resources;

public class EncryptionInvalidPasswordException()
    : EncryptionException(
        EncryptionErrorCode.InvalidPassword,
        message: Messages.InvalidPassword)
{
}
