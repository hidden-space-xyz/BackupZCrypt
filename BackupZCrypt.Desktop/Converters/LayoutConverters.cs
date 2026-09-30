using System.Globalization;

using Avalonia.Data.Converters;

namespace BackupZCrypt.Desktop.Converters;

/// <summary>
/// Value converters that adapt a layout to the space it is given.
/// </summary>
internal static class LayoutConverters
{
    /// <summary>
    /// The converter that turns an available width into the number of columns of at least the width named by
    /// the converter parameter that fit in it, so a grid of option tiles drops to fewer, wider columns in a
    /// narrow window instead of squeezing every tile.
    /// </summary>
    public static readonly FuncValueConverter<double, string?, int> ColumnsForWidth = new(
        static (width, minimumColumnWidth) => ColumnCount(width, minimumColumnWidth)
    );

    /// <summary>
    /// Computes how many columns of at least <paramref name="minimumColumnWidth"/> fit in <paramref name="width"/>.
    /// </summary>
    /// <param name="width">The available width in device-independent pixels.</param>
    /// <param name="minimumColumnWidth">The narrowest acceptable column, as an invariant-culture number.</param>
    /// <returns>The number of columns; at least one, including before the first layout reports a width.</returns>
    internal static int ColumnCount(double width, string? minimumColumnWidth)
    {
        if (
            !double.TryParse(
                minimumColumnWidth,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var minimum
            )
            || minimum <= 0
            || double.IsNaN(width)
            || double.IsInfinity(width)
        )
        {
            return 1;
        }

        return Math.Max(1, (int)Math.Floor(width / minimum));
    }
}
