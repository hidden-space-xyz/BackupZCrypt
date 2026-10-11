using Avalonia.Data.Converters;
using Avalonia.Media;

using BackupZCrypt.Desktop.Assets;
using BackupZCrypt.Desktop.ViewModels;

namespace BackupZCrypt.Desktop.Converters;

/// <summary>
/// Value converters that draw the icons the ViewModels name.
/// </summary>
internal static class NavigationIconConverters
{
    /// <summary>
    /// The converter that turns a <see cref="NavigationIcon"/> into the geometry the sidebar draws.
    /// </summary>
    public static readonly FuncValueConverter<NavigationIcon, StreamGeometry> ToGeometry = new(
        static icon => GeometryOf(icon)
    );

    /// <summary>
    /// Gets the geometry drawn for a navigation icon.
    /// </summary>
    /// <param name="icon">The icon to draw.</param>
    /// <returns>The icon's geometry.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="icon"/> names no icon.</exception>
    private static StreamGeometry GeometryOf(NavigationIcon icon)
    {
        return icon switch
        {
            NavigationIcon.CreateBackup => Icons.ShieldLock,
            NavigationIcon.UpdateBackup => Icons.ArrowSync,
            NavigationIcon.RestoreBackup => Icons.BoxArrowDown,
            NavigationIcon.VerifyBackup => Icons.ShieldCheck,
            NavigationIcon.Settings => Icons.Settings,
            NavigationIcon.About => Icons.Info,
            _ => throw new ArgumentOutOfRangeException(nameof(icon), icon, "No icon is drawn for this value."),
        };
    }
}
