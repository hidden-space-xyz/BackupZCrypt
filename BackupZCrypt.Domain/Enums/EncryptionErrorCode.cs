namespace BackupZCrypt.Domain.Enums;

public enum EncryptionErrorCode
{
    AccessDenied = 0,
    FileNotFound = 1,
    InsufficientDiskSpace = 2,
    InvalidPassword = 3,
    FileCorruption = 4,
    KeyDerivationFailed = 5,
    CipherOperationFailed = 6,
    Unknown = 7,
}
