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
    [Theory]
    [InlineData(0d)]
    [InlineData(-5d)]
    internal void ComputeEstimatedDuration_NonPositiveThroughput_ReturnsMaxValue(double throughput)
    {
        var result = BackupBenchmarkService.ComputeEstimatedDuration(
            TimeSpan.Zero,
            throughput,
            1000
        );

        Assert.Equal(TimeSpan.MaxValue, result);
    }

    [Theory]
    [InlineData(1d, 1000d, 2000L, 3d)]
    [InlineData(0d, 1000d, 1000L, 1d)]
    [InlineData(0d, 1000d, 2000L, 2d)]
    [InlineData(2.5d, 500d, 250L, 3d)]
    [InlineData(0.25d, 4d, 3L, 1d)]
    internal void ComputeEstimatedDuration_NormalInputs_SumsKeyDerivationAndComputeTime(
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

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), result);
    }

    [Fact]
    internal void ComputeEstimatedDuration_Overflow_ClampsOnlyWhenUnrepresentable()
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

        Assert.Multiple(
            () => Assert.Equal(TimeSpan.MaxValue, overflowing),
            () =>
                Assert.True(
                    representable < TimeSpan.MaxValue,
                    "the negative control for the clamp: roughly 29 years is still a representable TimeSpan, so a "
                        + "clamp applied too eagerly, or compared against the wrong constant, would report "
                        + "'unknown' for every large but perfectly valid backup"
                ),
            () => Assert.True(representable > TimeSpan.Zero)
        );
    }
}
