namespace BackupZCrypt.Domain.ValueObjects.Localization;

/// <summary>
/// Language-neutral identifiers for user-facing messages produced by the lower layers.
/// The presentation layer (Desktop) owns the translation: each member name maps to a
/// resx key of the same name in Strings.resx. Members whose name ends in "Format" expect
/// <see cref="string.Format(System.IFormatProvider, string, object?[])"/> arguments carried
/// by <see cref="LocalizableMessage"/>.
/// </summary>
public enum MessageCode
{
    /// <summary>
    /// The source path was not provided.
    /// </summary>
    SourcePathEmpty,

    /// <summary>
    /// The source path does not exist.
    /// </summary>
    SourcePathNotExist,

    /// <summary>
    /// The source path does not exist; formatted with the offending path.
    /// </summary>
    SourcePathNotExistFormat,

    /// <summary>
    /// The source file is empty.
    /// </summary>
    SourceFileEmpty,

    /// <summary>
    /// The source directory contains no files.
    /// </summary>
    SourceDirectoryEmpty,

    /// <summary>
    /// Access to the source was denied.
    /// </summary>
    SourceAccessDenied,

    /// <summary>
    /// An error occurred while accessing the source; formatted with error detail.
    /// </summary>
    SourceAccessErrorFormat,

    /// <summary>
    /// The destination path was not provided.
    /// </summary>
    DestinationPathEmpty,

    /// <summary>
    /// The destination drive is not accessible; formatted with the drive identifier.
    /// </summary>
    DestinationDriveNotAccessibleFormat,

    /// <summary>
    /// The destination path is invalid; formatted with the offending path.
    /// </summary>
    DestinationInvalidFormat,

    /// <summary>
    /// The source and destination refer to the same file.
    /// </summary>
    SourceDestinationSameFile,

    /// <summary>
    /// The source and destination refer to the same directory.
    /// </summary>
    SourceDestinationSameDirectory,

    /// <summary>
    /// The destination is located inside the source directory.
    /// </summary>
    DestinationInsideSource,

    /// <summary>
    /// The source is located inside the destination directory.
    /// </summary>
    SourceInsideDestination,

    /// <summary>
    /// A path is invalid; formatted with the offending path.
    /// </summary>
    InvalidPathFormat,

    /// <summary>
    /// A password is required.
    /// </summary>
    PasswordRequired,

    /// <summary>
    /// The password is shorter than the minimum length.
    /// </summary>
    PasswordTooShort,

    /// <summary>
    /// The password exceeds the maximum length.
    /// </summary>
    PasswordTooLong,

    /// <summary>
    /// The password has leading or trailing spaces.
    /// </summary>
    PasswordLeadingTrailingSpaces,

    /// <summary>
    /// The password confirmation is required.
    /// </summary>
    ConfirmPasswordRequired,

    /// <summary>
    /// The password and its confirmation do not match.
    /// </summary>
    PasswordMismatch,

    /// <summary>
    /// The destination drive is low on free space; formatted with the space detail.
    /// </summary>
    LowDiskSpaceFormat,

    /// <summary>
    /// The operation involves a large amount of data; formatted with the size detail.
    /// </summary>
    LargeOperationFormat,

    /// <summary>
    /// The operation involves a moderate amount of data; formatted with the size detail.
    /// </summary>
    MediumOperationFormat,

    /// <summary>
    /// The destination already contains files; formatted with the count or detail.
    /// </summary>
    DestinationExistingFilesFormat,

    /// <summary>
    /// The chosen password is weak.
    /// </summary>
    WeakPasswordWarning,

    /// <summary>
    /// The source directory contains no files to back up.
    /// </summary>
    NoFilesInSourceDirectory,

    /// <summary>
    /// Encryption of a file failed; formatted with error detail.
    /// </summary>
    EncryptionErrorFormat,

    /// <summary>
    /// Every file in the operation failed to process.
    /// </summary>
    AllFilesFailed,

    /// <summary>
    /// Writing the manifest failed; formatted with error detail.
    /// </summary>
    ManifestWriteFailedFormat,

    /// <summary>
    /// A manifest is required to perform an update.
    /// </summary>
    ManifestRequiredForUpdate,

    /// <summary>
    /// A manifest is required to perform decryption.
    /// </summary>
    ManifestRequiredForDecryption,

    /// <summary>
    /// The update source must be a directory.
    /// </summary>
    UpdateSourceMustBeDirectory,

    /// <summary>
    /// The backup destination must already exist.
    /// </summary>
    BackupDestinationMustExist,

    /// <summary>
    /// The supplied password is incorrect.
    /// </summary>
    InvalidPassword,

    /// <summary>
    /// An unexpected error occurred; formatted with error detail.
    /// </summary>
    UnexpectedErrorFormat,

    /// <summary>
    /// Strength label for a very weak password.
    /// </summary>
    StrengthVeryWeak,

    /// <summary>
    /// Strength label for a weak password.
    /// </summary>
    StrengthWeak,

    /// <summary>
    /// Strength label for a fair password.
    /// </summary>
    StrengthFair,

    /// <summary>
    /// Strength label for a good password.
    /// </summary>
    StrengthGood,

    /// <summary>
    /// Strength label for a strong password.
    /// </summary>
    StrengthStrong,

    /// <summary>
    /// Estimated password entropy; formatted with the entropy value.
    /// </summary>
    EntropyFormat,

    /// <summary>
    /// Header for the list of password improvement suggestions.
    /// </summary>
    Suggestions,

    /// <summary>
    /// Positive confirmation that the password needs no improvement.
    /// </summary>
    GoodJob,

    /// <summary>
    /// Tip suggesting the password be made longer.
    /// </summary>
    TipIncreaseLength,

    /// <summary>
    /// Tip suggesting uppercase letters be added.
    /// </summary>
    TipAddUppercase,

    /// <summary>
    /// Tip suggesting lowercase letters be added.
    /// </summary>
    TipAddLowercase,

    /// <summary>
    /// Tip suggesting digits be added.
    /// </summary>
    TipAddDigits,

    /// <summary>
    /// Tip suggesting symbols be added.
    /// </summary>
    TipAddSymbols,

    /// <summary>
    /// Tip suggesting a greater variety of character classes.
    /// </summary>
    TipMoreVariety,

    /// <summary>
    /// Tip suggesting predictable character sequences be avoided.
    /// </summary>
    TipAvoidSequences,

    /// <summary>
    /// Tip suggesting repeated characters be reduced.
    /// </summary>
    TipReduceRepeats,

    /// <summary>
    /// Tip suggesting recognizable years be avoided.
    /// </summary>
    TipAvoidYears,
}
