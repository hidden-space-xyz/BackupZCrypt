namespace BackupZCrypt.Domain.Exceptions;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Resources;

public class AccessDeniedException : EncryptionException
{
    public AccessDeniedException()
        : base(BackupErrorCode.AccessDenied)
    {
    }

    public AccessDeniedException(string? message)
        : base(BackupErrorCode.AccessDenied, message)
    {
    }

    public AccessDeniedException(string? message, Exception innerException)
        : base(BackupErrorCode.AccessDenied, message, innerException)
    {
    }

    public static AccessDeniedException CreateForFilePath(
        string filePath,
        Exception innerException) =>
        new(string.Format(Messages.AccessDeniedFormat, filePath), innerException);

    protected AccessDeniedException(
        BackupErrorCode code,
        string? message = null)
        : base(code, message)
    {
    }

    protected AccessDeniedException(
        BackupErrorCode code,
        string? message,
        Exception innerException)
        : base(code, message, innerException)
    {
    }
}
