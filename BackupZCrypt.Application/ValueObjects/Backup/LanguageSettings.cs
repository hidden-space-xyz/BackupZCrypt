using BackupZCrypt.Application.Services.Interfaces;

namespace BackupZCrypt.Application.ValueObjects.Backup;

public sealed record LanguageSettings(string? LanguageCode = null) : ISettings<LanguageSettings>
{
    public static LanguageSettings DefaultValue { get; } = new();

    public static string FileName => "language-settings.json";
}
