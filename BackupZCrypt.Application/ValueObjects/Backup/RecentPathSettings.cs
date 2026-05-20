using BackupZCrypt.Application.Services.Interfaces;

namespace BackupZCrypt.Application.ValueObjects.Backup;

public sealed record RecentPathSettings(
    string? LastSourcePath = null,
    string? LastDestinationPath = null)
    : ISettings<RecentPathSettings>
{
    public static RecentPathSettings DefaultValue { get; } = new();

    public static string FileName => "recent-path-settings.json";
}
