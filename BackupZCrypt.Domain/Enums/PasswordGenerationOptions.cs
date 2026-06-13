namespace BackupZCrypt.Domain.Enums;

/// <summary>
/// Flags that control which character classes a generated password may contain.
/// </summary>
[Flags]
public enum PasswordGenerationOptions
{
    /// <summary>
    /// No options enabled.
    /// </summary>
    None = 0,

    /// <summary>
    /// Allow uppercase letters in the generated password.
    /// </summary>
    IncludeUppercase = 1,

    /// <summary>
    /// Allow lowercase letters in the generated password.
    /// </summary>
    IncludeLowercase = 1 << 1,

    /// <summary>
    /// Allow numeric digits in the generated password.
    /// </summary>
    IncludeNumbers = 1 << 2,

    /// <summary>
    /// Allow special (symbol) characters in the generated password.
    /// </summary>
    IncludeSpecialCharacters = 1 << 3,

    /// <summary>
    /// Omit visually ambiguous characters to reduce transcription errors.
    /// </summary>
    ExcludeSimilarCharacters = 1 << 4,
}
