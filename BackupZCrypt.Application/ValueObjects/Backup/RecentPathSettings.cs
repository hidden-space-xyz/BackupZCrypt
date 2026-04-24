namespace BackupZCrypt.Application.ValueObjects.Backup;

public sealed record RecentPathSettings(
    string? LastSourcePath = null,
    string? LastDestinationPath = null)
{
    public static RecentPathSettings Default { get; } = new();
}
