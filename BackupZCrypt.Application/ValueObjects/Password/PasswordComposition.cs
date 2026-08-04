namespace BackupZCrypt.Application.ValueObjects.Password;

/// <summary>
/// Describes which character classes are present in a password.
/// </summary>
/// <param name="HasUpper">Whether the password contains an uppercase ASCII letter.</param>
/// <param name="HasLower">Whether the password contains a lowercase ASCII letter.</param>
/// <param name="HasDigit">Whether the password contains an ASCII decimal digit.</param>
/// <param name="HasSpecial">Whether the password contains one of the recognized ASCII punctuation or symbol characters.</param>
/// <param name="HasOther">Whether the password contains a non-ASCII (Unicode) character.</param>
public sealed record class PasswordComposition(
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
        Convert.ToInt32(this.HasUpper)
        + Convert.ToInt32(this.HasLower)
        + Convert.ToInt32(this.HasDigit)
        + Convert.ToInt32(this.HasSpecial)
        + Convert.ToInt32(this.HasOther);
}
