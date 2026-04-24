namespace BackupZCrypt.Domain.Enums;

public enum ValidationErrorCode
{
    /// <summary>
    /// Generic
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// FileProcessingResult
    /// </summary>
    ElapsedTimeNegative = 1,
    TotalBytesNegative = 2,
    ProcessedFilesNegative = 3,
    TotalFilesNegative = 4,
    ProcessedFilesExceedTotalFiles = 5,

    /// <summary>
    /// FileProcessingStatus
    /// </summary>
    ProcessedBytesNegative = 6,
    ProcessedBytesExceedTotalBytes = 7,
    ElapsedNegative = 8,

    /// <summary>
    /// PasswordService.GeneratePassword
    /// </summary>
    PasswordLengthNonPositive = 9,
    PasswordOptionsNone = 10,
    NoCharactersAvailableForGeneration = 11,
}
