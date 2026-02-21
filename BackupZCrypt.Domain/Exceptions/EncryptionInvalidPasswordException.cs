namespace BackupZCrypt.Domain.Exceptions;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Resources;

public class EncryptionInvalidPasswordException()
    : EncryptionException(
        EncryptionErrorCode.InvalidPassword,
        message: Messages.InvalidPassword)
{
}
