using BackupZCrypt.Domain.Enums;

namespace BackupZCrypt.Application.ValueObjects.Password;

public sealed record PasswordStrengthAnalysis(
    PasswordStrength Strength,
    string Description,
    double Score);
