namespace BackupZCrypt.Domain.Exceptions;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Resources;

public class CipherException : EncryptionException
{
    public CipherException()
        : base(BackupErrorCode.CipherOperationFailed)
    {
    }

    public CipherException(string? message)
        : base(BackupErrorCode.CipherOperationFailed, message)
    {
    }

    public CipherException(string? message, Exception innerException)
        : base(BackupErrorCode.CipherOperationFailed, message, innerException)
    {
    }

    public static CipherException CreateForOperation(
        string operation,
        Exception innerException) =>
        new(string.Format(Messages.CipherOperationFailedFormat, operation), innerException);

    protected CipherException(
        BackupErrorCode code,
        string? message = null)
        : base(code, message)
    {
    }

    protected CipherException(
        BackupErrorCode code,
        string? message,
        Exception innerException)
        : base(code, message, innerException)
    {
    }
}
