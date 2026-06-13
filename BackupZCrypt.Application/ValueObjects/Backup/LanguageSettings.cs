using BackupZCrypt.Application.Services.Interfaces;

namespace BackupZCrypt.Application.ValueObjects.Backup;

/// <summary>
/// Persisted UI language preference.
/// </summary>
/// <param name="LanguageCode">The selected culture code, or <see langword="null"/> to follow the system default.</param>
public sealed record LanguageSettings(string? LanguageCode = null) : ISettings<LanguageSettings>
{
    /// <summary>
    /// Gets the default settings used when none have been persisted.
    /// </summary>
    public static LanguageSettings DefaultValue { get; } = new();

    /// <summary>
    /// Gets the file name under which these settings are stored.
    /// </summary>
    public static string FileName => "language-settings.json";
}
