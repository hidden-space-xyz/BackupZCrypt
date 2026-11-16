using System.Globalization;

using Avalonia.Media;

using BackupZCrypt.Desktop.Assets;
using BackupZCrypt.Desktop.Converters;
using BackupZCrypt.Desktop.ViewModels;
using BackupZCrypt.Test.Common;

namespace BackupZCrypt.Test.Unit.Desktop.Converters;

/// <summary>
/// Unit tests for <see cref="NavigationIconConverters"/>: every navigation entry draws its own icon, and a value
/// that names no icon is refused rather than drawn as an empty shape.
/// </summary>
public sealed class NavigationIconConvertersTests
{
    [Fact]
    internal Task ToGeometry_DrawsTheIconOfEveryEntry()
    {
        return HeadlessUi.RunAsync(static () =>
        {
            Dictionary<NavigationIcon, StreamGeometry> expected = new()
            {
                [NavigationIcon.CreateBackup] = Icons.ShieldLock,
                [NavigationIcon.UpdateBackup] = Icons.ArrowSync,
                [NavigationIcon.RestoreBackup] = Icons.BoxArrowDown,
                [NavigationIcon.VerifyBackup] = Icons.ShieldCheck,
                [NavigationIcon.Settings] = Icons.Settings,
                [NavigationIcon.About] = Icons.Info,
            };

            Assert.Multiple(
                () => Assert.Equal(Enum.GetValues<NavigationIcon>().Order(), expected.Keys.Order()),
                () => Assert.All(expected, pair => Assert.Same(pair.Value, Convert(pair.Key))),
                () => Assert.Equal(expected.Count, expected.Values.Distinct().Count())
            );
        });
    }

    [Fact]
    internal Task ToGeometry_ValueThatNamesNoIcon_Throws()
    {
        return HeadlessUi.RunAsync(static () =>
            Assert.Throws<ArgumentOutOfRangeException>(static () => Convert((NavigationIcon)999)));
    }

    /// <summary>
    /// Converts an icon the way a binding does.
    /// </summary>
    /// <param name="icon">The icon.</param>
    /// <returns>The converted value.</returns>
    private static object? Convert(NavigationIcon icon)
    {
        return NavigationIconConverters.ToGeometry.Convert(
            icon,
            typeof(Geometry),
            null,
            CultureInfo.InvariantCulture
        );
    }
}
