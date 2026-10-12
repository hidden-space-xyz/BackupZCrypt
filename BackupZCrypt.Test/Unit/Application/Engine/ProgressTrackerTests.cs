using System.Diagnostics;

using BackupZCrypt.Application.Common;
using BackupZCrypt.Application.Engine;
using BackupZCrypt.Test.Common;

namespace BackupZCrypt.Test.Unit.Application.Engine;

/// <summary>
/// Unit tests for <see cref="ProgressTracker"/>, which turns the bytes and files a run gets through
/// into status reports that never exceed the totals measured before the run.
/// </summary>
public sealed class ProgressTrackerTests
{
    [Fact]
    internal void Reports_StartAtZeroAndFollowTheBytesAndFilesCompleted()
    {
        RecordingProgress<BackupStatus> progress = new();
        ProgressTracker tracker = new(progress, Stopwatch.StartNew(), 2, 100);

        tracker.ReportStart();
        tracker.AddBytes(40);
        tracker.CompleteFile();

        Assert.Multiple(
            () => Assert.Equal(3, progress.Reports.Count),
            () => Assert.Equal((0, 0L), (progress.Reports[0].ProcessedFiles, progress.Reports[0].ProcessedBytes)),
            () => Assert.Equal((0, 40L), (progress.Reports[1].ProcessedFiles, progress.Reports[1].ProcessedBytes)),
            () => Assert.Equal((1, 40L), (progress.Reports[2].ProcessedFiles, progress.Reports[2].ProcessedBytes)),
            () => Assert.All(progress.Reports, static r => Assert.Equal((2, 100L), (r.TotalFiles, r.TotalBytes)))
        );
    }

    [Fact]
    internal void Reports_AFileThatGrewWhileItWasRead_NeverExceedTheMeasuredTotals()
    {
        RecordingProgress<BackupStatus> progress = new();
        ProgressTracker tracker = new(progress, Stopwatch.StartNew(), 1, 100);

        tracker.AddBytes(250);
        tracker.CompleteFile();
        tracker.CompleteFile();

        Assert.All(
            progress.Reports,
            static r =>
                Assert.True(
                    r.ProcessedBytes <= r.TotalBytes && r.ProcessedFiles <= r.TotalFiles,
                    "A report claimed more work than the run was started with."
                )
        );
    }

    [Fact]
    internal void Reports_WithoutAProgressSink_AreSilentlyDropped()
    {
        ProgressTracker tracker = new(null, Stopwatch.StartNew(), 1, 10);

        var exception = Record.Exception(() =>
        {
            tracker.ReportStart();
            tracker.AddBytes(10);
            tracker.CompleteFile();
        });

        Assert.Null(exception);
    }
}
