namespace BackupZCrypt.Desktop.Models;

/// <summary>
/// A selectable UI language choice.
/// </summary>
/// <param name="Code">The culture code (for example, <c>en</c>, <c>es</c>), or <see langword="null"/> for the system default.</param>
/// <param name="Name">The display name shown to the user.</param>
internal sealed record class LanguageOption(string? Code, string Name)
{
    /// <summary>
    /// Returns the display name, which is also what screen readers announce for the option.
    /// </summary>
    /// <returns>The display name.</returns>
    public override string ToString()
    {
        return this.Name;
    }
}
