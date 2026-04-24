namespace BackupZCrypt.Domain.Exceptions;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Resources;

public class EncryptionInvalidPasswordException : EncryptionException
{
    public EncryptionInvalidPasswordException(string? message)
        : base(EncryptionErrorCode.InvalidPassword, message)
    {
    }

    public EncryptionInvalidPasswordException(string? message, Exception innerException)
        : base(EncryptionErrorCode.InvalidPassword, message, innerException)
    {
    }

    public EncryptionInvalidPasswordException()
        : base(
            EncryptionErrorCode.InvalidPassword,
            Messages.InvalidPassword)
    {
    }

    protected EncryptionInvalidPasswordException(
        EncryptionErrorCode code,
        string? message = null)
        : base(code, message)
    {
    }

    protected EncryptionInvalidPasswordException(
        EncryptionErrorCode code,
        string? message,
        Exception innerException)
        : base(code, message, innerException)
    {
    }
}
