namespace BackupZCrypt.Domain.Enums;

public enum ValidationErrorCode
{
    // Generic
    Unknown,

    // FileProcessingResult
    ElapsedTimeNegative,
    TotalBytesNegative,
    ProcessedFilesNegative,
    TotalFilesNegative,
    ProcessedFilesExceedTotalFiles,

    // FileProcessingStatus
    ProcessedBytesNegative,
    ProcessedBytesExceedTotalBytes,
    ElapsedNegative,

    // PasswordService.GeneratePassword
    PasswordLengthNonPositive,
    PasswordOptionsNone,
    NoCharactersAvailableForGeneration,
}
