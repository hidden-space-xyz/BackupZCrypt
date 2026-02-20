namespace CloudZCrypt.Application.ValueObjects.Password;

using CloudZCrypt.Domain.Enums;

public sealed record PasswordStrengthAnalysis(
    PasswordStrength Strength,
    string Description,
    double Score);
