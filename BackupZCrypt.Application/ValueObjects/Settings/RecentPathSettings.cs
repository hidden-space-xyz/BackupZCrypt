using BackupZCrypt.Domain.Services.Interfaces;

namespace BackupZCrypt.Application.ValueObjects.Settings;

/// <summary>
/// Persisted most-recently-used source and destination paths to prefill the UI.
/// </summary>
/// <param name="LastSourcePath">The last source path the user selected, or <see langword="null"/> if none.</param>
/// <param name="LastDestinationPath">The last destination path the user selected, or <see langword="null"/> if none.</param>
public sealed record class RecentPathSettings(
    string? LastSourcePath = null,
    string? LastDestinationPath = null
) : ISettings<RecentPathSettings>
{
    /// <summary>
    /// Gets the default settings used when none have been persisted.
    /// </summary>
    public static RecentPathSettings DefaultValue { get; } = new();

    /// <summary>
    /// Gets the file name under which these settings are stored.
    /// </summary>
    public static string FileName => "recent-path-settings.json";
}
