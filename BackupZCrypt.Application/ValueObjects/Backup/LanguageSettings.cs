namespace BackupZCrypt.Application.ValueObjects.Backup;

using BackupZCrypt.Application.Services.Interfaces;

public sealed record LanguageSettings(
    string? LanguageCode = null)
    : ISettings<LanguageSettings>
{
    public static LanguageSettings DefaultValue { get; } = new();

    public static string FileName => "language-settings.json";
}
