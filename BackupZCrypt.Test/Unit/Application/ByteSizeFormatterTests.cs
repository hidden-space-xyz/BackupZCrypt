using System.Globalization;
using BackupZCrypt.Application.Utilities.Formatters;

namespace BackupZCrypt.Test.Unit.Application;

public sealed class ByteSizeFormatterTests
{
    [Fact]
    public void Format_Zero_ReturnsZeroBytes()
    {
        // Zero is culture-independent (no number formatting), so no culture override needed.
        Assert.Equal("0 B", ByteSizeFormatter.Format(0));
    }

    [Theory]
    [InlineData(512L, "512.0 B")]
    [InlineData(1024L, "1.0 KB")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(1_048_576L, "1.0 MB")]
    [InlineData(1_073_741_824L, "1.0 GB")]
    [InlineData(1_099_511_627_776L, "1.0 TB")]
    public void Format_ScalesByUnitWithOneDecimal_UnderInvariantCulture(long bytes, string expected)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            Assert.Equal(expected, ByteSizeFormatter.Format(bytes));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Format_PetabyteScaleStaysInTerabytes()
    {
        // The suffix table tops out at TB, so larger values stay in TB rather than overflowing.
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            // 2 PB == 2048 TB.
            Assert.Equal("2048.0 TB", ByteSizeFormatter.Format(1_099_511_627_776L * 2048L));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Format_NegativeBytes_UsesAbsoluteValue()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            Assert.Equal("1.0 KB", ByteSizeFormatter.Format(-1024));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
