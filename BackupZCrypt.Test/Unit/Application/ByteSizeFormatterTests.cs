using BackupZCrypt.Application.Utilities.Formatters;
using BackupZCrypt.Test.Common;

namespace BackupZCrypt.Test.Unit.Application;

/// <summary>
/// Unit tests for the byte-size formatter.
/// </summary>
/// <remarks>
/// The formatter interpolates with the ambient culture, so "1.5 KB" here is "1,5 KB" under a
/// comma-decimal locale. The cases are therefore pinned with <c>[SetCulture("")]</c> rather than by
/// assigning <c>CultureInfo.CurrentCulture</c>: the attribute installs the culture before the test
/// and restores it afterwards, so the class holds no shared mutable state and cannot leak a locale
/// into any other test.
/// </remarks>
public sealed class ByteSizeFormatterTests
{
    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(512L, "512.0 B")]
    [InlineData(1023L, "1023.0 B")]
    [InlineData(1024L, "1.0 KB")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(1_048_576L, "1.0 MB")]
    [InlineData(1_073_741_824L, "1.0 GB")]
    [InlineData(1_099_511_627_776L, "1.0 TB")]
    [InlineData(1_099_511_627_776L * 2048L, "2048.0 TB")]
    [InlineData(-1024L, "1.0 KB")]
    [SetCulture("")]
    internal void Format_ByMagnitude_ScalesToLargestFittingUnit(long bytes, string expected)
    {
        Assert.Equal(expected, ByteSizeFormatter.Format(bytes));
    }
}
