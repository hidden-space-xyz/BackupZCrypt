namespace BackupZCrypt.Domain.Enums;

[Flags]
public enum PasswordGenerationOptions
{
    None = 0,
    IncludeUppercase = 1,
    IncludeLowercase = 1 << 1,
    IncludeNumbers = 1 << 2,
    IncludeSpecialCharacters = 1 << 3,
    ExcludeSimilarCharacters = 1 << 4,
}
