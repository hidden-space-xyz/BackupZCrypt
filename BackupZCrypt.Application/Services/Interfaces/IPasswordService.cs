using BackupZCrypt.Application.ValueObjects.Password;
using BackupZCrypt.Domain.Enums;

namespace BackupZCrypt.Application.Services.Interfaces;

/// <summary>
/// Analyzes password strength and generates random passwords from configurable character sets.
/// </summary>
public interface IPasswordService
{
    /// <summary>
    /// Evaluates a password's entropy and strength, returning a score and improvement tips.
    /// </summary>
    /// <param name="password">The password to analyze.</param>
    /// <returns>An analysis containing the strength rating, score, entropy, and localizable tips.</returns>
    PasswordStrengthAnalysis AnalyzePasswordStrength(string password);

    /// <summary>
    /// Generates a cryptographically random password of the requested length.
    /// </summary>
    /// <param name="length">The number of characters the generated password must contain.</param>
    /// <param name="options">Flags selecting which character classes to include and exclusions to apply.</param>
    /// <returns>A randomly generated password.</returns>
    string GeneratePassword(int length, PasswordGenerationOptions options);
}
