namespace BackupZCrypt.Domain.Constants;

/// <summary>
/// The bounds a backup password must satisfy.
/// </summary>
/// <remarks>
/// These live in one place because two layers must agree on them: the request validator rejects a
/// password outside the bounds, and the create page's start command greys itself out on the same
/// rule. A command gate has to answer synchronously and so cannot call the async validator; if the
/// two ever disagreed, the button would offer an operation the validator then refuses, or refuse one
/// it would have accepted.
/// </remarks>
public static class PasswordConstants
{
    /// <summary>
    /// The shortest accepted password, in characters.
    /// </summary>
    public const int MinLength = 8;

    /// <summary>
    /// The longest accepted password, in characters. The cap exists because the password is fed to a
    /// deliberately expensive key derivation function.
    /// </summary>
    public const int MaxLength = 1000;
}
