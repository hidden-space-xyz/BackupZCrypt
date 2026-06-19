using Avalonia.Data.Converters;

namespace BackupZCrypt.Desktop.Converters;

/// <summary>
/// Value converters used by password input fields.
/// </summary>
internal static class PasswordConverters
{
    /// <summary>
    /// Maps the "reveal password" flag to the value bound to <c>TextBox.PasswordChar</c>: the default
    /// character (<c>'\0'</c>, no masking) when the password is revealed, or a bullet when it is
    /// hidden. Clearing the password character also re-enables copy and cut, which Avalonia disables
    /// while a field is masked — so the password can be copied only while it is visible.
    /// </summary>
    public static readonly FuncValueConverter<bool, char> RevealToPasswordChar = new(
        static reveal => reveal ? '\0' : '●'
    );
}
