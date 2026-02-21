namespace BackupZCrypt.Domain.Enums;

[Flags]
public enum PasswordGenerationOptions
{
    None = 0,
    IncludeUppercase = 1,
    IncludeLowercase = 2,
    IncludeNumbers = 4,
    IncludeSpecialCharacters = 8,
    ExcludeSimilarCharacters = 16,
}
