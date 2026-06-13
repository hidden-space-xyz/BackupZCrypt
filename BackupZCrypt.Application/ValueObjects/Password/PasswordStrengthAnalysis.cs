using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Application.ValueObjects.Password;

public sealed record PasswordStrengthAnalysis(
    PasswordStrength Strength,
    double Score,
    double Entropy,
    IReadOnlyList<MessageCode> Tips
);
