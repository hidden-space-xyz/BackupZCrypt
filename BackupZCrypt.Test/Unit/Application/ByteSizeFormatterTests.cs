using System.Globalization;

using BackupZCrypt.Application.Utilities.Formatters;

namespace BackupZCrypt.Test.Unit.Application;

/// <summary>
/// Unit tests for the byte-size formatter.
/// </summary>
public sealed class ByteSizeFormatterTests
{
    [Test]
    public void Format_Zero_ReturnsZeroBytes()
    {
        Assert.That(ByteSizeFormatter.Format(0), Is.EqualTo("0 B"));
    }

    [TestCase(512L, "512.0 B")]
    [TestCase(1024L, "1.0 KB")]
    [TestCase(1536L, "1.5 KB")]
    [TestCase(1_048_576L, "1.0 MB")]
    [TestCase(1_073_741_824L, "1.0 GB")]
    [TestCase(1_099_511_627_776L, "1.0 TB")]
    public void Format_ScalesByUnitWithOneDecimal_UnderInvariantCulture(long bytes, string expected)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            Assert.That(ByteSizeFormatter.Format(bytes), Is.EqualTo(expected));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Test]
    public void Format_PetabyteScaleStaysInTerabytes()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            Assert.That(ByteSizeFormatter.Format(1_099_511_627_776L * 2048L), Is.EqualTo("2048.0 TB"));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Test]
    public void Format_NegativeBytes_UsesAbsoluteValue()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            Assert.That(ByteSizeFormatter.Format(-1024), Is.EqualTo("1.0 KB"));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
