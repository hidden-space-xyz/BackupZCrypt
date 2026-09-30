using BackupZCrypt.Desktop.Converters;

namespace BackupZCrypt.Test.Unit.Desktop;

/// <summary>
/// Unit tests for <see cref="LayoutConverters"/>. The column count drives the option tile grids of the settings
/// page, so it must shrink with the window, never reach zero, and fall back to a single column when it is
/// asked before layout has measured anything or is given no usable minimum.
/// </summary>
public sealed class LayoutConvertersTests
{
    [Theory]
    [InlineData(772, 3)]
    [InlineData(690, 3)]
    [InlineData(689.9, 2)]
    [InlineData(584, 2)]
    [InlineData(229, 1)]
    [InlineData(0, 1)]
    internal void ColumnCount_FitsAsManyColumnsOfTheMinimumWidthAsTheSpaceAllows(double width, int expected)
    {
        Assert.Equal(expected, LayoutConverters.ColumnCount(width, "230"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wide")]
    [InlineData("0")]
    [InlineData("-50")]
    internal void ColumnCount_WithoutAUsableMinimumWidth_UsesASingleColumn(string? minimumColumnWidth)
    {
        Assert.Equal(1, LayoutConverters.ColumnCount(772, minimumColumnWidth));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    internal void ColumnCount_WithAnUnmeasuredWidth_UsesASingleColumn(double width)
    {
        Assert.Equal(1, LayoutConverters.ColumnCount(width, "230"));
    }
}
