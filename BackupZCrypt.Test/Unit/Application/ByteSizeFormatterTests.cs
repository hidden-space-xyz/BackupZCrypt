using BackupZCrypt.Application.Utilities.Formatters;

namespace BackupZCrypt.Test.Unit.Application;

/// <summary>
/// Unit tests for the byte-size formatter.
/// </summary>
/// <remarks>
/// The formatter interpolates with the ambient culture, so "1.5 KB" here is "1,5 KB" under a
/// comma-decimal locale. The cases are therefore pinned with <c>[SetCulture("")]</c> rather than by
/// assigning <c>CultureInfo.CurrentCulture</c>: the attribute is scoped to the test by NUnit, so the
/// fixture holds no shared mutable state and can run in parallel with any other fixture.
/// </remarks>
public sealed class ByteSizeFormatterTests
{
    [TestCase(0L, "0 B")]
    [TestCase(512L, "512.0 B")]
    [TestCase(1023L, "1023.0 B")]
    [TestCase(1024L, "1.0 KB")]
    [TestCase(1536L, "1.5 KB")]
    [TestCase(1_048_576L, "1.0 MB")]
    [TestCase(1_073_741_824L, "1.0 GB")]
    [TestCase(1_099_511_627_776L, "1.0 TB")]
    [TestCase(1_099_511_627_776L * 2048L, "2048.0 TB")]
    [TestCase(-1024L, "1.0 KB")]
    [SetCulture("")]
    public void Format_ByMagnitude_ScalesToLargestFittingUnit(long bytes, string expected)
    {
        Assert.That(ByteSizeFormatter.Format(bytes), Is.EqualTo(expected));
    }
}
