using BackupZCrypt.Application.Utilities.Formatters;

namespace BackupZCrypt.Test.Unit.Application;

/// <summary>
/// Unit tests for the duration formatter.
/// </summary>
/// <remarks>
/// <para>
/// The formatter interpolates with the ambient culture, so every formatting case is pinned with
/// <c>[SetCulture("")]</c> rather than by assigning <c>CultureInfo.CurrentCulture</c>. Previously only
/// the sub-second case was pinned while the whole-second cases relied on integers below 1000 rendering
/// identically in every locale, which would have broken the moment a decimal appeared in them.
/// </para>
/// <para>
/// The table deliberately straddles each branch of the five-way cascade: for every threshold there is a
/// row just below it and a row exactly on it, so flipping any <c>&lt;</c> to <c>&lt;=</c> fails.
/// </para>
/// </remarks>
public sealed class DurationFormatterTests
{
    [Test]
    public void Format_Negative_Throws()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => DurationFormatter.Format(TimeSpan.FromSeconds(-1))
        );
    }

    [TestCase(0d, "0.0 s")]
    [TestCase(0.4d, "0.4 s")]
    [TestCase(0.999d, "1.0 s")]
    [TestCase(1d, "1 s")]
    [TestCase(45.7d, "45 s")]
    [TestCase(59.999d, "59 s")]
    [TestCase(60d, "1 min 0 s")]
    [TestCase(200d, "3 min 20 s")]
    [TestCase(3599d, "59 min 59 s")]
    [TestCase(3600d, "1 h 0 min")]
    [TestCase(7500d, "2 h 5 min")]
    [TestCase(86_399d, "23 h 59 min")]
    [TestCase(86_400d, "1 d 0 h")]
    [TestCase(97_200d, "1 d 3 h")]
    [TestCase(34_578_000d, "400 d 5 h")]
    [SetCulture("")]
    public void Format_ByMagnitude_UsesExpectedGranularity(double seconds, string expected)
    {
        Assert.That(DurationFormatter.Format(TimeSpan.FromSeconds(seconds)), Is.EqualTo(expected));
    }
}
