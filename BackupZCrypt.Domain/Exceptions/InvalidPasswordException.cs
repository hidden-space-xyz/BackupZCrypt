namespace BackupZCrypt.Domain.Exceptions;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Resources;

public class InvalidPasswordException : EncryptionException
{
    public InvalidPasswordException(string? message)
        : base(BackupErrorCode.InvalidPassword, message)
    {
    }

    public InvalidPasswordException(string? message, Exception innerException)
        : base(BackupErrorCode.InvalidPassword, message, innerException)
    {
    }

    public InvalidPasswordException()
        : base(
            BackupErrorCode.InvalidPassword,
            Messages.InvalidPassword)
    {
    }

    protected InvalidPasswordException(
        BackupErrorCode code,
        string? message = null)
        : base(code, message)
    {
    }

    protected InvalidPasswordException(
        BackupErrorCode code,
        string? message,
        Exception innerException)
        : base(code, message, innerException)
    {
    }
}
