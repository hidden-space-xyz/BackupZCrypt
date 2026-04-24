namespace BackupZCrypt.Domain.Exceptions;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Resources;

public class EncryptionAccessDeniedException : EncryptionException
{
    public EncryptionAccessDeniedException()
        : base(EncryptionErrorCode.AccessDenied)
    {
    }

    public EncryptionAccessDeniedException(string? message)
        : base(EncryptionErrorCode.AccessDenied, message)
    {
    }

    public EncryptionAccessDeniedException(string? message, Exception innerException)
        : base(EncryptionErrorCode.AccessDenied, message, innerException)
    {
    }

    public static EncryptionAccessDeniedException CreateForFilePath(
        string filePath,
        Exception innerException) =>
        new(string.Format(Messages.AccessDeniedFormat, filePath), innerException);

    protected EncryptionAccessDeniedException(
        EncryptionErrorCode code,
        string? message = null)
        : base(code, message)
    {
    }

    protected EncryptionAccessDeniedException(
        EncryptionErrorCode code,
        string? message,
        Exception innerException)
        : base(code, message, innerException)
    {
    }
}
