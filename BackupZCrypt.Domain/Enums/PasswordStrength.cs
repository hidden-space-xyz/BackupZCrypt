namespace BackupZCrypt.Domain.Enums;

/// <summary>
/// Rates the estimated strength of a password from weakest to strongest.
/// </summary>
public enum PasswordStrength
{
    /// <summary>
    /// Trivially guessable; offers almost no protection.
    /// </summary>
    VeryWeak = 0,

    /// <summary>
    /// Easily guessable and not recommended.
    /// </summary>
    Weak = 1,

    /// <summary>
    /// Marginal strength that could be improved.
    /// </summary>
    Fair = 2,

    /// <summary>
    /// Reasonably resistant to guessing.
    /// </summary>
    Good = 3,

    /// <summary>
    /// Highly resistant to guessing.
    /// </summary>
    Strong = 4,
}
