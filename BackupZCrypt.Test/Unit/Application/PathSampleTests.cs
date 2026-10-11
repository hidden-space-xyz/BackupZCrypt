using BackupZCrypt.Application.Common;

namespace BackupZCrypt.Test.Unit.Application;

/// <summary>
/// Unit tests for <see cref="PathSample"/>, the handful of example paths a warning about many files
/// names.
/// </summary>
public sealed class PathSampleTests
{
    [Fact]
    internal void Of_FewPaths_NamesThemAllInOrder()
    {
        var sample = PathSample.Of(["b.txt", "a.txt"]);

        Assert.Multiple(
            () => Assert.Equal(["b.txt", "a.txt"], sample.Paths),
            () => Assert.False(sample.IsTruncated)
        );
    }

    [Fact]
    internal void Of_ExactlyFivePaths_IsNotCutShort()
    {
        var sample = PathSample.Of(["1", "2", "3", "4", "5"]);

        Assert.Multiple(
            () => Assert.Equal(5, sample.Paths.Count),
            () => Assert.False(sample.IsTruncated)
        );
    }

    [Fact]
    internal void Of_MoreThanFivePaths_NamesTheFirstFiveAndIsCutShort()
    {
        var sample = PathSample.Of(["1", "2", "3", "4", "5", "6"]);

        Assert.Multiple(
            () => Assert.Equal(["1", "2", "3", "4", "5"], sample.Paths),
            () => Assert.True(sample.IsTruncated)
        );
    }

    [Fact]
    internal void Of_ReadsNoMoreOfTheSequenceThanItNeeds()
    {
        var sample = PathSample.Of(Endless());

        Assert.True(sample.IsTruncated);
    }

    [Fact]
    internal void Equality_ComparesThePathsAndWhetherTheSampleWasCutShort()
    {
        Assert.Multiple(
            () => Assert.Equal(PathSample.Of(["a"]), PathSample.Of(["a"])),
            () => Assert.Equal(PathSample.Of(["a"]).GetHashCode(), PathSample.Of(["a"]).GetHashCode()),
            () => Assert.NotEqual(PathSample.Of(["a"]), PathSample.Of(["A"])),
            () => Assert.NotEqual(PathSample.Of(["1", "2", "3", "4", "5"]), PathSample.Of(["1", "2", "3", "4", "5", "6"])),
            () => Assert.False(PathSample.Of(["a"]).Equals(null)),
            () => Assert.False(PathSample.Of(["a"]).Equals((object)"a"))
        );
    }

    [Fact]
    internal void Of_WithoutPaths_IsRejected()
    {
        _ = Assert.Throws<ArgumentNullException>(() => PathSample.Of(null!));
    }

    /// <summary>
    /// Yields paths without end, so a sample that read it to completion would never return.
    /// </summary>
    /// <returns>The endless sequence.</returns>
    private static IEnumerable<string> Endless()
    {
        for (var i = 0; ; i++)
        {
            yield return i.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
