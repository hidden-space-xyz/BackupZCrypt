using BackupZCrypt.Application.Services;

namespace BackupZCrypt.Test.Unit.Application;

/// <summary>
/// Unit tests for the benchmark service's estimated-duration computation.
/// </summary>
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

    [Test]
    public void ComputeEstimatedDuration_NormalInputs_SumsKeyDerivationAndComputeTime()
    {
        var result = BackupBenchmarkService.ComputeEstimatedDuration(
            TimeSpan.FromSeconds(1),
            throughputBytesPerSecond: 1000,
            dataBytes: 2000
        );

        Assert.That(result, Is.EqualTo(TimeSpan.FromSeconds(3)));
    }

    [Test]
    public void ComputeEstimatedDuration_Overflow_ClampsToMaxValue()
    {
        var result = BackupBenchmarkService.ComputeEstimatedDuration(
            TimeSpan.Zero,
            throughputBytesPerSecond: 1.0,
            dataBytes: long.MaxValue
        );

        Assert.That(result, Is.EqualTo(TimeSpan.MaxValue));
    }

    [Test]
    public void ComputeEstimatedDuration_ScalesWithDataBytes()
    {
        var single = BackupBenchmarkService.ComputeEstimatedDuration(TimeSpan.Zero, 1000, 1000);
        var doubled = BackupBenchmarkService.ComputeEstimatedDuration(TimeSpan.Zero, 1000, 2000);

        Assert.That(doubled, Is.GreaterThan(single));
    }
}
