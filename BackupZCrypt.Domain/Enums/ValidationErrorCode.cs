namespace BackupZCrypt.Domain.Enums;

public enum ValidationErrorCode
{
    // Generic
    Unknown = 0,

    // FileProcessingResult
    ElapsedTimeNegative = 1,
    TotalBytesNegative = 2,
    ProcessedFilesNegative = 3,
    TotalFilesNegative = 4,
    ProcessedFilesExceedTotalFiles = 5,

    // FileProcessingStatus
    ProcessedBytesNegative = 6,
    ProcessedBytesExceedTotalBytes = 7,
    ElapsedNegative = 8,

    // PasswordService.GeneratePassword
    PasswordLengthNonPositive = 9,
    PasswordOptionsNone = 10,
    NoCharactersAvailableForGeneration = 11,
}
