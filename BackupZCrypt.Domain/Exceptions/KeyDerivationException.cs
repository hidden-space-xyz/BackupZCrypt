namespace BackupZCrypt.Domain.Exceptions;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Resources;

public class KeyDerivationException : EncryptionException
{
    public KeyDerivationException()
        : base(BackupErrorCode.KeyDerivationFailed)
    {
    }

    public KeyDerivationException(string? message)
        : base(BackupErrorCode.KeyDerivationFailed, message)
    {
    }

    public KeyDerivationException(string? message, Exception innerException)
        : base(BackupErrorCode.KeyDerivationFailed, message, innerException)
    {
    }

    public KeyDerivationException(Exception innerException)
        : base(
            BackupErrorCode.KeyDerivationFailed,
            Messages.KeyDerivationFailed,
            innerException)
    {
    }

    protected KeyDerivationException(
        BackupErrorCode code,
        string? message = null)
        : base(code, message)
    {
    }

    protected KeyDerivationException(
        BackupErrorCode code,
        string? message,
        Exception innerException)
        : base(code, message, innerException)
    {
    }
}
