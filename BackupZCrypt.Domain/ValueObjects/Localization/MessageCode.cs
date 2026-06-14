namespace BackupZCrypt.Domain.ValueObjects.Localization;

/// <summary>
/// Language-neutral identifiers for user-facing messages produced by the lower layers.
/// The presentation layer (Desktop) owns the translation: each member name maps to a
/// resx key of the same name in Strings.resx. Members whose name ends in "Format" expect
/// <see cref="string.Format(IFormatProvider, string, object?[])"/> arguments carried
/// by <see cref="LocalizableMessage"/>.
/// </summary>
public enum MessageCode
{
    /// <summary>
    /// The source path was not provided.
    /// </summary>
    SourcePathEmpty = 0,

    /// <summary>
    /// The source path does not exist.
    /// </summary>
    SourcePathNotExist = 1,

    /// <summary>
    /// The source path does not exist; formatted with the offending path.
    /// </summary>
    SourcePathNotExistFormat = 2,

    /// <summary>
    /// The source directory contains no files.
    /// </summary>
    SourceDirectoryEmpty = 4,

    /// <summary>
    /// Access to the source was denied.
    /// </summary>
    SourceAccessDenied = 5,

    /// <summary>
    /// An error occurred while accessing the source; formatted with error detail.
    /// </summary>
    SourceAccessErrorFormat = 6,

    /// <summary>
    /// The destination path was not provided.
    /// </summary>
    DestinationPathEmpty = 7,

    /// <summary>
    /// The destination drive is not accessible; formatted with the drive identifier.
    /// </summary>
    DestinationDriveNotAccessibleFormat = 8,

    /// <summary>
    /// The destination path is invalid; formatted with the offending path.
    /// </summary>
    DestinationInvalidFormat = 9,

    /// <summary>
    /// The source and destination refer to the same directory.
    /// </summary>
    SourceDestinationSameDirectory = 11,

    /// <summary>
    /// The destination is located inside the source directory.
    /// </summary>
    DestinationInsideSource = 12,

    /// <summary>
    /// The source is located inside the destination directory.
    /// </summary>
    SourceInsideDestination = 13,

    /// <summary>
    /// A path is invalid; formatted with the offending path.
    /// </summary>
    InvalidPathFormat = 14,

    /// <summary>
    /// A password is required.
    /// </summary>
    PasswordRequired = 15,

    /// <summary>
    /// The password is shorter than the minimum length.
    /// </summary>
    PasswordTooShort = 16,

    /// <summary>
    /// The password exceeds the maximum length.
    /// </summary>
    PasswordTooLong = 17,

    /// <summary>
    /// The password has leading or trailing spaces.
    /// </summary>
    PasswordLeadingTrailingSpaces = 18,

    /// <summary>
    /// The password confirmation is required.
    /// </summary>
    ConfirmPasswordRequired = 19,

    /// <summary>
    /// The password and its confirmation do not match.
    /// </summary>
    PasswordMismatch = 20,

    /// <summary>
    /// The destination drive is low on free space; formatted with the space detail.
    /// </summary>
    LowDiskSpaceFormat = 21,

    /// <summary>
    /// The operation involves a large amount of data; formatted with the size detail.
    /// </summary>
    LargeOperationFormat = 22,

    /// <summary>
    /// The operation involves a moderate amount of data; formatted with the size detail.
    /// </summary>
    MediumOperationFormat = 23,

    /// <summary>
    /// The destination already contains files; formatted with the count or detail.
    /// </summary>
    DestinationExistingFilesFormat = 24,

    /// <summary>
    /// The chosen password is weak.
    /// </summary>
    WeakPasswordWarning = 25,

    /// <summary>
    /// The source directory contains no files to back up.
    /// </summary>
    NoFilesInSourceDirectory = 26,

    /// <summary>
    /// Encryption of a file failed; formatted with error detail.
    /// </summary>
    EncryptionErrorFormat = 27,

    /// <summary>
    /// Every file in the operation failed to process.
    /// </summary>
    AllFilesFailed = 28,

    /// <summary>
    /// Writing the manifest failed; formatted with error detail.
    /// </summary>
    ManifestWriteFailedFormat = 29,

    /// <summary>
    /// A manifest is required to perform an update.
    /// </summary>
    ManifestRequiredForUpdate = 30,

    /// <summary>
    /// A manifest is required to perform decryption.
    /// </summary>
    ManifestRequiredForDecryption = 31,

    /// <summary>
    /// The backup destination must already exist.
    /// </summary>
    BackupDestinationMustExist = 33,

    /// <summary>
    /// The supplied password is incorrect.
    /// </summary>
    InvalidPassword = 34,

    /// <summary>
    /// An unexpected error occurred; formatted with error detail.
    /// </summary>
    UnexpectedErrorFormat = 35,

    /// <summary>
    /// Strength label for a very weak password.
    /// </summary>
    StrengthVeryWeak = 36,

    /// <summary>
    /// Strength label for a weak password.
    /// </summary>
    StrengthWeak = 37,

    /// <summary>
    /// Strength label for a fair password.
    /// </summary>
    StrengthFair = 38,

    /// <summary>
    /// Strength label for a good password.
    /// </summary>
    StrengthGood = 39,

    /// <summary>
    /// Strength label for a strong password.
    /// </summary>
    StrengthStrong = 40,

    /// <summary>
    /// Estimated password entropy; formatted with the entropy value.
    /// </summary>
    EntropyFormat = 41,

    /// <summary>
    /// Header for the list of password improvement suggestions.
    /// </summary>
    Suggestions = 42,

    /// <summary>
    /// Positive confirmation that the password needs no improvement.
    /// </summary>
    GoodJob = 43,

    /// <summary>
    /// Tip suggesting the password be made longer.
    /// </summary>
    TipIncreaseLength = 44,

    /// <summary>
    /// Tip suggesting uppercase letters be added.
    /// </summary>
    TipAddUppercase = 45,

    /// <summary>
    /// Tip suggesting lowercase letters be added.
    /// </summary>
    TipAddLowercase = 46,

    /// <summary>
    /// Tip suggesting digits be added.
    /// </summary>
    TipAddDigits = 47,

    /// <summary>
    /// Tip suggesting symbols be added.
    /// </summary>
    TipAddSymbols = 48,

    /// <summary>
    /// Tip suggesting a greater variety of character classes.
    /// </summary>
    TipMoreVariety = 49,

    /// <summary>
    /// Tip suggesting predictable character sequences be avoided.
    /// </summary>
    TipAvoidSequences = 50,

    /// <summary>
    /// Tip suggesting repeated characters be reduced.
    /// </summary>
    TipReduceRepeats = 51,

    /// <summary>
    /// Tip suggesting recognizable years be avoided.
    /// </summary>
    TipAvoidYears = 52,

    /// <summary>
    /// The source must be a directory rather than a single file.
    /// </summary>
    SourceMustBeDirectory = 53,
}
