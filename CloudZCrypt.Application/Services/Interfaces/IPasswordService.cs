using CloudZCrypt.Application.ValueObjects.Password;
using CloudZCrypt.Domain.Enums;

namespace CloudZCrypt.Application.Services.Interfaces
{
    public interface IPasswordService
    {
        PasswordStrengthAnalysis AnalyzePasswordStrength(string password);

        string GeneratePassword(int length, PasswordGenerationOptions options);
    }
}
