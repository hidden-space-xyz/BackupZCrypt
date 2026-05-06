namespace BackupZCrypt.Domain.Exceptions;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Resources;

public class InsufficientSpaceException : EncryptionException
{
    public InsufficientSpaceException()
        : base(BackupErrorCode.InsufficientDiskSpace)
    {
    }

    public InsufficientSpaceException(string? message)
        : base(BackupErrorCode.InsufficientDiskSpace, message)
    {
    }

    public InsufficientSpaceException(string? message, Exception innerException)
        : base(BackupErrorCode.InsufficientDiskSpace, message, innerException)
    {
    }

    public static InsufficientSpaceException CreateForPath(string path) =>
        new(string.Format(Messages.InsufficientDiskSpaceFormat, path));

    protected InsufficientSpaceException(
        BackupErrorCode code,
        string? message = null)
        : base(code, message)
    {
    }

    protected InsufficientSpaceException(
        BackupErrorCode code,
        string? message,
        Exception innerException)
        : base(code, message, innerException)
    {
    }
}
