namespace BackupZCrypt.Application.Passwords;

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
    public PasswordStrengthAnalysis AnalyzePasswordStrength(string password);

    /// <summary>
    /// Generates a cryptographically random password of the requested length.
    /// </summary>
    /// <param name="length">The number of characters the generated password must contain.</param>
    /// <param name="options">The flags selecting which character classes to include and exclusions to apply.</param>
    /// <returns>A randomly generated password.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="length"/> is zero or negative.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="options"/> selects no character class, or its exclusions leave no character to
    /// draw from.
    /// </exception>
    public string GeneratePassword(int length, PasswordGenerationOptions options);
}
