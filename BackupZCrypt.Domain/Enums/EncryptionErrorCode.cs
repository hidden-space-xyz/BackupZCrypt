namespace BackupZCrypt.Domain.Enums;

public enum EncryptionErrorCode
{
    AccessDenied,
    FileNotFound,
    InsufficientDiskSpace,
    InvalidPassword,
    FileCorruption,
    KeyDerivationFailed,
    CipherOperationFailed,
    Unknown,
}
