using BackupZCrypt.Application.Services;

namespace BackupZCrypt.Test.Unit.Application;

/// <summary>
/// Unit tests for the benchmark service's estimated-duration computation.
/// </summary>
/// <remarks>
/// Every expected value below is exactly representable as a <see cref="TimeSpan"/>, so the assertions
/// can stay exact instead of hiding a formula regression behind a tolerance.
/// </remarks>
public sealed class BackupBenchmarkServiceComputeTests
{
    [TestCase(0d)]
    [TestCase(-5d)]
    public void ComputeEstimatedDuration_NonPositiveThroughput_ReturnsMaxValue(double throughput)
    {
        var result = BackupBenchmarkService.ComputeEstimatedDuration(
            TimeSpan.Zero,
            throughput,
            1000
        );

        Assert.That(result, Is.EqualTo(TimeSpan.MaxValue));
    }

    [TestCase(1d, 1000d, 2000L, 3d)]
    [TestCase(0d, 1000d, 1000L, 1d)]
    [TestCase(0d, 1000d, 2000L, 2d)]
    [TestCase(2.5d, 500d, 250L, 3d)]
    [TestCase(0.25d, 4d, 3L, 1d)]
    public void ComputeEstimatedDuration_NormalInputs_SumsKeyDerivationAndComputeTime(
        double keyDerivationSeconds,
        double throughputBytesPerSecond,
        long dataBytes,
        double expectedSeconds
    )
    {
        var result = BackupBenchmarkService.ComputeEstimatedDuration(
            TimeSpan.FromSeconds(keyDerivationSeconds),
            throughputBytesPerSecond,
            dataBytes
        );

        Assert.That(result, Is.EqualTo(TimeSpan.FromSeconds(expectedSeconds)));
    }

    [Test]
    public void ComputeEstimatedDuration_Overflow_ClampsOnlyWhenUnrepresentable()
    {
        var overflowing = BackupBenchmarkService.ComputeEstimatedDuration(
            TimeSpan.Zero,
            throughputBytesPerSecond: 1.0,
            dataBytes: long.MaxValue
        );

        var representable = BackupBenchmarkService.ComputeEstimatedDuration(
            TimeSpan.Zero,
            throughputBytesPerSecond: 1e10,
            dataBytes: long.MaxValue
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(overflowing, Is.EqualTo(TimeSpan.MaxValue));
            Assert.That(
                representable,
                Is.LessThan(TimeSpan.MaxValue),
                "the negative control for the clamp: roughly 29 years is still a representable TimeSpan, so a "
                    + "clamp applied too eagerly, or compared against the wrong constant, would report "
                    + "'unknown' for every large but perfectly valid backup"
            );
            Assert.That(representable, Is.GreaterThan(TimeSpan.Zero));
        }
    }
}
