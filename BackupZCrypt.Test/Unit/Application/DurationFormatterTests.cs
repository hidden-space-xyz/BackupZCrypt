using System.Globalization;

using BackupZCrypt.Application.Utilities.Formatters;

namespace BackupZCrypt.Test.Unit.Application;

public sealed class DurationFormatterTests
{
    [Test]
    public void Format_Negative_Throws()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => DurationFormatter.Format(TimeSpan.FromSeconds(-1))
        );
    }

    [Test]
    public void Format_SubSecond_ShowsOneDecimalSecond()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(DurationFormatter.Format(TimeSpan.Zero), Is.EqualTo("0.0 s"));
                Assert.That(
                    DurationFormatter.Format(TimeSpan.FromMilliseconds(400)),
                    Is.EqualTo("0.4 s")
                );
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Test]
    public void Format_UnderOneMinute_ShowsWholeSeconds()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(DurationFormatter.Format(TimeSpan.FromSeconds(1)), Is.EqualTo("1 s"));
            Assert.That(DurationFormatter.Format(TimeSpan.FromSeconds(45.7)), Is.EqualTo("45 s"));
        }
    }

    [Test]
    public void Format_UnderOneHour_ShowsMinutesAndSeconds()
    {
        Assert.That(
            DurationFormatter.Format(TimeSpan.FromSeconds(200)),
            Is.EqualTo("3 min 20 s")
        );
    }

    [Test]
    public void Format_UnderOneDay_ShowsHoursAndMinutes()
    {
        Assert.That(
            DurationFormatter.Format(TimeSpan.FromMinutes(125)),
            Is.EqualTo("2 h 5 min")
        );
    }

    [Test]
    public void Format_OneDayOrMore_ShowsDaysAndHours()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(DurationFormatter.Format(TimeSpan.FromHours(27)), Is.EqualTo("1 d 3 h"));
            Assert.That(
                DurationFormatter.Format(TimeSpan.FromDays(400) + TimeSpan.FromHours(5)),
                Is.EqualTo("400 d 5 h")
            );
        }
    }
}
