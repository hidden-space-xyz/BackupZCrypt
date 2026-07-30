using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Application.ValueObjects.Password;

/// <summary>
/// The result of analyzing a password's strength.
/// </summary>
/// <param name="Strength">The categorical strength rating derived from the score.</param>
/// <param name="Score">The normalized strength score from 0 to 100.</param>
/// <param name="Entropy">The estimated effective entropy in bits after penalties.</param>
/// <param name="Tips">The localizable suggestions for improving the password.</param>
public sealed record PasswordStrengthAnalysis(
    PasswordStrength Strength,
    double Score,
    double Entropy,
    IReadOnlyList<MessageCode> Tips
);
