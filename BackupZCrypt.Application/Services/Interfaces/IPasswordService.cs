using BackupZCrypt.Application.ValueObjects.Password;
using BackupZCrypt.Domain.Enums;

namespace BackupZCrypt.Application.Services.Interfaces;

public interface IPasswordService
{
    PasswordStrengthAnalysis AnalyzePasswordStrength(string password);

    string GeneratePassword(int length, PasswordGenerationOptions options);
}
