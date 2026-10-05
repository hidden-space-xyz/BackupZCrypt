using BackupZCrypt.Application.Services;
using BackupZCrypt.Application.Utilities.Helpers;

namespace BackupZCrypt.Test.Unit.Application;

/// <summary>
/// Unit tests pinning the number of workers the benchmark's timed pass runs to the number of file
/// pipelines a real backup runs at once.
/// </summary>
/// <remarks>
/// A backup never runs more than <see cref="FileParallelismPolicy.MaximumDegreeOfParallelism"/> file
/// pipelines and processes the chunks of each file one after another, so a timed pass with one worker
/// per logical processor multiplies the CPU-bound throughput by processors the backup leaves idle and,
/// on a many-core machine, reports a duration several times too short. The worker count is checked for
/// explicit processor counts rather than the host's, so the regression is caught on every runner,
/// including those with no more logical processors than the cap, where the two numbers coincide.
/// </remarks>
public sealed class BackupBenchmarkServiceWorkerCountTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(24)]
    internal void ComputeWorkerCount_AnyProcessorCount_MatchesTheFilePipelinesOfARealBackup(
        int processorCount
    )
    {
        var workerCount = BackupBenchmarkService.ComputeWorkerCount(processorCount);

        Assert.Multiple(
            () => Assert.Equal(FileParallelismPolicy.ForProcessorCount(processorCount), workerCount),
            () => Assert.InRange(workerCount, 1, FileParallelismPolicy.MaximumDegreeOfParallelism)
        );
    }
}
