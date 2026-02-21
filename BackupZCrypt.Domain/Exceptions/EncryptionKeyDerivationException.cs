namespace BackupZCrypt.Domain.Exceptions;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Resources;

public class EncryptionKeyDerivationException(Exception innerException)
    : EncryptionException(
        EncryptionErrorCode.KeyDerivationFailed,
        message: Messages.KeyDerivationFailed,
        innerException)
{
}
