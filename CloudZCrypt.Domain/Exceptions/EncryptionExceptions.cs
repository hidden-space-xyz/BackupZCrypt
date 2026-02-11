using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Resources;

namespace CloudZCrypt.Domain.Exceptions;

public abstract class EncryptionException : Exception
{
    public EncryptionErrorCode Code { get; }

    protected EncryptionException(EncryptionErrorCode code, string? message = null)
        : base(message ?? code.ToString())
    {
        Code = code;
    }

    protected EncryptionException(
        EncryptionErrorCode code,
        string? message,
        Exception innerException
    )
        : base(message ?? code.ToString(), innerException)
    {
        Code = code;
    }
}

public class EncryptionAccessDeniedException(string filePath, Exception innerException)
    : EncryptionException(
        EncryptionErrorCode.AccessDenied,
        message: string.Format(Messages.AccessDeniedFormat, filePath),
        innerException
    ) { }

public class EncryptionFileNotFoundException(string filePath)
    : EncryptionException(
        EncryptionErrorCode.FileNotFound,
        message: string.Format(Messages.FileNotFoundFormat, filePath)
    ) { }

public class EncryptionInsufficientSpaceException(string path)
    : EncryptionException(
        EncryptionErrorCode.InsufficientDiskSpace,
        message: string.Format(Messages.InsufficientDiskSpaceFormat, path)
    ) { }

public class EncryptionInvalidPasswordException()
    : EncryptionException(
        EncryptionErrorCode.InvalidPassword,
        message: Messages.InvalidPassword
    ) { }

public class EncryptionCorruptedFileException(string filePath)
    : EncryptionException(
        EncryptionErrorCode.FileCorruption,
        message: string.Format(Messages.CorruptedFileFormat, filePath)
    ) { }

public class EncryptionKeyDerivationException(Exception innerException)
    : EncryptionException(
        EncryptionErrorCode.KeyDerivationFailed,
        message: Messages.KeyDerivationFailed,
        innerException
    ) { }

public class EncryptionCipherException(string operation, Exception innerException)
    : EncryptionException(
        EncryptionErrorCode.CipherOperationFailed,
        message: string.Format(Messages.CipherOperationFailedFormat, operation),
        innerException
    ) { }
