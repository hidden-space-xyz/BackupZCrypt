namespace BackupZCrypt.Application.ValueObjects.Password;

/// <summary>
/// Describes which character classes are present in a password.
/// </summary>
/// <param name="HasUpper">Whether the password contains an uppercase ASCII letter.</param>
/// <param name="HasLower">Whether the password contains a lowercase ASCII letter.</param>
/// <param name="HasDigit">Whether the password contains a digit.</param>
/// <param name="HasSpecial">Whether the password contains a special character.</param>
/// <param name="HasOther">Whether the password contains a non-ASCII (Unicode) character.</param>
public sealed record PasswordComposition(
    bool HasUpper,
    bool HasLower,
    bool HasDigit,
    bool HasSpecial,
    bool HasOther
)
{
    /// <summary>
    /// Gets the number of distinct character classes present in the password.
    /// </summary>
    public int CategoryCount =>
        (this.HasUpper ? 1 : 0)
        + (this.HasLower ? 1 : 0)
        + (this.HasDigit ? 1 : 0)
        + (this.HasSpecial ? 1 : 0)
        + (this.HasOther ? 1 : 0);
}
