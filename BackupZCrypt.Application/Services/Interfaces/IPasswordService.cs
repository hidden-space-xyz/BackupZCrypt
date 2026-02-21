namespace BackupZCrypt.Application.Services.Interfaces
{
    using BackupZCrypt.Application.ValueObjects.Password;
    using BackupZCrypt.Domain.Enums;

    public interface IPasswordService
    {
        PasswordStrengthAnalysis AnalyzePasswordStrength(string password);

        string GeneratePassword(int length, PasswordGenerationOptions options);
    }
}
