namespace BackupZCrypt.Application.ValueObjects.Password;

using BackupZCrypt.Domain.Enums;

public sealed record PasswordStrengthAnalysis(
    PasswordStrength Strength,
    string Description,
    double Score);
