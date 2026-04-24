namespace BackupZCrypt.Domain.Exceptions;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Resources;

public class EncryptionInsufficientSpaceException : EncryptionException
{
    public EncryptionInsufficientSpaceException()
        : base(EncryptionErrorCode.InsufficientDiskSpace)
    {
    }

    public EncryptionInsufficientSpaceException(string? message)
        : base(EncryptionErrorCode.InsufficientDiskSpace, message)
    {
    }

    public EncryptionInsufficientSpaceException(string? message, Exception innerException)
        : base(EncryptionErrorCode.InsufficientDiskSpace, message, innerException)
    {
    }

    public static EncryptionInsufficientSpaceException CreateForPath(string path) =>
        new(string.Format(Messages.InsufficientDiskSpaceFormat, path));

    protected EncryptionInsufficientSpaceException(
        EncryptionErrorCode code,
        string? message = null)
        : base(code, message)
    {
    }

    protected EncryptionInsufficientSpaceException(
        EncryptionErrorCode code,
        string? message,
        Exception innerException)
        : base(code, message, innerException)
    {
    }
}
