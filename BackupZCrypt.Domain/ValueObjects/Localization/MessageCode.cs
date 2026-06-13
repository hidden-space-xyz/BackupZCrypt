namespace BackupZCrypt.Domain.ValueObjects.Localization;

// Language-neutral identifiers for user-facing messages produced by the lower
// layers. The presentation layer (Desktop) owns the translation: each member
// name maps to a resx key of the same name in Strings.resx. Members whose name
// ends in "Format" expect string.Format arguments carried by LocalizableMessage.
public enum MessageCode
{
    // Validation — source/destination
    SourcePathEmpty,
    SourcePathNotExist,
    SourcePathNotExistFormat,
    SourceFileEmpty,
    SourceDirectoryEmpty,
    SourceAccessDenied,
    SourceAccessErrorFormat,
    DestinationPathEmpty,
    DestinationDriveNotAccessibleFormat,
    DestinationInvalidFormat,
    SourceDestinationSameFile,
    SourceDestinationSameDirectory,
    DestinationInsideSource,
    SourceInsideDestination,
    InvalidPathFormat,

    // Validation — password
    PasswordRequired,
    PasswordTooShort,
    PasswordTooLong,
    PasswordLeadingTrailingSpaces,
    ConfirmPasswordRequired,
    PasswordMismatch,

    // Warnings
    LowDiskSpaceFormat,
    LargeOperationFormat,
    MediumOperationFormat,
    DestinationExistingFilesFormat,
    WeakPasswordWarning,

    // Backup / restore operation
    NoFilesInSourceDirectory,
    EncryptionErrorFormat,
    AllFilesFailed,
    ManifestWriteFailedFormat,
    ManifestRequiredForUpdate,
    ManifestRequiredForDecryption,
    UpdateSourceMustBeDirectory,
    BackupDestinationMustExist,
    InvalidPassword,
    UnexpectedErrorFormat,

    // Password strength — labels
    StrengthVeryWeak,
    StrengthWeak,
    StrengthFair,
    StrengthGood,
    StrengthStrong,
    EntropyFormat,
    Suggestions,
    GoodJob,

    // Password strength — tips
    TipIncreaseLength,
    TipAddUppercase,
    TipAddLowercase,
    TipAddDigits,
    TipAddSymbols,
    TipMoreVariety,
    TipAvoidSequences,
    TipReduceRepeats,
    TipAvoidYears,
}
