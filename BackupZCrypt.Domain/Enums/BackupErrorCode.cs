namespace BackupZCrypt.Domain.Enums;

/// <summary>
/// Categorizes the cause of a failed backup or restore operation.
/// </summary>
public enum BackupErrorCode
{
    /// <summary>
    /// The operating system denied access to a required file or directory.
    /// </summary>
    AccessDenied = 0,

    /// <summary>
    /// An expected file could not be located.
    /// </summary>
    FileNotFound = 1,

    /// <summary>
    /// The destination does not have enough free space to complete the operation.
    /// </summary>
    InsufficientDiskSpace = 2,

    /// <summary>
    /// The supplied password is incorrect or authentication failed.
    /// </summary>
    InvalidPassword = 3,

    /// <summary>
    /// Stored data is damaged or fails integrity verification.
    /// </summary>
    FileCorruption = 4,

    /// <summary>
    /// Deriving a key from the password failed.
    /// </summary>
    KeyDerivationFailed = 5,

    /// <summary>
    /// An encryption or decryption cipher operation failed.
    /// </summary>
    CipherOperationFailed = 6,

    /// <summary>
    /// The failure does not match any known category.
    /// </summary>
    Unknown = 7,
}
