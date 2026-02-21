namespace BackupZCrypt.Application.ValueObjects.Password;

public sealed record PasswordComposition(
    bool HasUpper,
    bool HasLower,
    bool HasDigit,
    bool HasSpecial,
    bool HasOther)
{
    public int CategoryCount =>
        (this.HasUpper ? 1 : 0)
        + (this.HasLower ? 1 : 0)
        + (this.HasDigit ? 1 : 0)
        + (this.HasSpecial ? 1 : 0)
        + (this.HasOther ? 1 : 0);
}
