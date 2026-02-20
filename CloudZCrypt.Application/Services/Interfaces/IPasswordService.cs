namespace CloudZCrypt.Application.Services.Interfaces
{
    using CloudZCrypt.Application.ValueObjects.Password;
    using CloudZCrypt.Domain.Enums;

    public interface IPasswordService
    {
        PasswordStrengthAnalysis AnalyzePasswordStrength(string password);

        string GeneratePassword(int length, PasswordGenerationOptions options);
    }
}
