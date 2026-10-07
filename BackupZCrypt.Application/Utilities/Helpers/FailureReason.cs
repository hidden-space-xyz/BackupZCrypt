using System.Security.Cryptography;

using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Application.Utilities.Helpers;

/// <summary>
/// Translates the exception behind a per-file or per-folder failure into a language-neutral reason,
/// so the user reads why a file was skipped in their own language instead of a raw system message.
/// </summary>
internal static class FailureReason
{
    /// <summary>
    /// The Windows HRESULT raised when another process holds the file open without sharing it.
    /// </summary>
    private const int SharingViolation = unchecked((int)0x80070020);

    /// <summary>
    /// The Windows HRESULT raised when another process holds a lock on a region of the file.
    /// </summary>
    private const int LockViolation = unchecked((int)0x80070021);

    /// <summary>
    /// The Windows HRESULT raised when the disk is full.
    /// </summary>
    private const int DiskFull = unchecked((int)0x80070070);

    /// <summary>
    /// The Windows HRESULT raised when the end of the disk was reached while writing.
    /// </summary>
    private const int HandleDiskFull = unchecked((int)0x80070027);

    /// <summary>
    /// The Unix error number for a full device, which .NET surfaces as the exception's HRESULT.
    /// </summary>
    private const int NoSpaceLeftOnDevice = 28;

    /// <summary>
    /// Maps an exception to the reason shown next to the affected path.
    /// </summary>
    /// <param name="exception">The exception that stopped the file or folder from being processed.</param>
    /// <returns>The localizable reason.</returns>
    internal static LocalizableMessage From(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            MissingChunkException => new LocalizableMessage(MessageCode.ReasonChunkMissing),
            UnauthorizedAccessException => new LocalizableMessage(MessageCode.ReasonAccessDenied),
            FileNotFoundException or DirectoryNotFoundException => new LocalizableMessage(
                MessageCode.ReasonFileNotFound
            ),
            PathTooLongException => new LocalizableMessage(MessageCode.ReasonPathTooLong),
            CryptographicException or InvalidDataException => new LocalizableMessage(
                MessageCode.ReasonDataCorrupted
            ),
            IOException { HResult: SharingViolation or LockViolation } => new LocalizableMessage(
                MessageCode.ReasonFileInUse
            ),
            IOException { HResult: DiskFull or HandleDiskFull or NoSpaceLeftOnDevice } =>
                new LocalizableMessage(MessageCode.ReasonDiskFull),
            _ => new LocalizableMessage(MessageCode.ReasonIoErrorFormat, exception.Message),
        };
    }
}
