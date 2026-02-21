namespace BackupZCrypt.Test.Application.Utilities;

using BackupZCrypt.Application.Utilities.Formatters;

[TestFixture]
internal sealed class ByteSizeFormatterTests
{
    [Test]
    public void Format_ZeroBytes_ReturnsZeroB()
    {
        Assert.That(ByteSizeFormatter.Format(0), Is.EqualTo("0 B"));
    }

    [Test]
    public void Format_Bytes_EndsWith_B()
    {
        string result = ByteSizeFormatter.Format(512);

        Assert.That(result, Does.EndWith(" B"));
        Assert.That(result, Does.Contain("512"));
    }

    [Test]
    public void Format_Kilobytes_EndsWith_KB()
    {
        string result = ByteSizeFormatter.Format(1024);

        Assert.That(result, Does.EndWith(" KB"));
    }

    [Test]
    public void Format_Megabytes_EndsWith_MB()
    {
        string result = ByteSizeFormatter.Format(1024 * 1024);

        Assert.That(result, Does.EndWith(" MB"));
    }

    [Test]
    public void Format_Gigabytes_EndsWith_GB()
    {
        string result = ByteSizeFormatter.Format(1024L * 1024 * 1024);

        Assert.That(result, Does.EndWith(" GB"));
    }

    [Test]
    public void Format_Terabytes_EndsWith_TB()
    {
        string result = ByteSizeFormatter.Format(1024L * 1024 * 1024 * 1024);

        Assert.That(result, Does.EndWith(" TB"));
    }

    [Test]
    public void Format_FractionalKilobytes_EndsWith_KB()
    {
        string result = ByteSizeFormatter.Format(1536);

        Assert.That(result, Does.EndWith(" KB"));
    }
}
