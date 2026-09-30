using BackupZCrypt.Desktop.Services;
using BackupZCrypt.Domain.ValueObjects.Backup;

namespace BackupZCrypt.Test.Unit.Desktop;

/// <summary>
/// Unit tests for <see cref="CoalescingStatusProgress"/>. The engine reports from several worker threads at
/// once, so the sink must never let the page see progress run backwards, must not queue one delivery per
/// report, and must still start over when a new phase announces different totals.
/// </summary>
public sealed class CoalescingStatusProgressTests
{
    /// <summary>
    /// The context the sink under test captures, which holds posted deliveries until the test runs them.
    /// </summary>
    private readonly QueuedSynchronizationContext context = new();

    [Fact]
    internal void Report_SeveralTimesBeforeTheContextRuns_QueuesASingleDeliveryOfTheFurthestStatus()
    {
        List<BackupStatus> delivered = [];
        var sut = CreateSut(this.context, delivered.Add);

        sut.Report(new BackupStatus(1, 10, 100, 1000, TimeSpan.FromSeconds(1)));
        sut.Report(new BackupStatus(3, 10, 300, 1000, TimeSpan.FromSeconds(3)));
        sut.Report(new BackupStatus(2, 10, 200, 1000, TimeSpan.FromSeconds(2)));

        var queued = this.context.Pending;
        this.context.RunAll();

        Assert.Multiple(
            () => Assert.Equal(1, queued),
            () =>
                Assert.Equal<BackupStatus>(
                    [new BackupStatus(3, 10, 300, 1000, TimeSpan.FromSeconds(3))],
                    delivered
                )
        );
    }

    [Fact]
    internal void Report_OutOfOrderAfterADelivery_NeverMovesTheProgressBackwards()
    {
        List<BackupStatus> delivered = [];
        var sut = CreateSut(this.context, delivered.Add);

        sut.Report(new BackupStatus(6, 10, 700, 1000, TimeSpan.FromSeconds(7)));
        this.context.RunAll();

        sut.Report(new BackupStatus(6, 10, 600, 1000, TimeSpan.FromSeconds(6)));
        sut.Report(new BackupStatus(5, 10, 650, 1000, TimeSpan.FromSeconds(5)));
        this.context.RunAll();

        Assert.Equal<BackupStatus>(
            [
                new BackupStatus(6, 10, 700, 1000, TimeSpan.FromSeconds(7)),
                new BackupStatus(6, 10, 700, 1000, TimeSpan.FromSeconds(7)),
            ],
            delivered
        );
    }

    [Fact]
    internal void Report_TakesEachCountFromWhicheverReportIsFurthestAlong()
    {
        List<BackupStatus> delivered = [];
        var sut = CreateSut(this.context, delivered.Add);

        sut.Report(new BackupStatus(7, 10, 400, 1000, TimeSpan.FromSeconds(2)));
        sut.Report(new BackupStatus(4, 10, 900, 1000, TimeSpan.FromSeconds(9)));
        this.context.RunAll();

        Assert.Equal<BackupStatus>(
            [new BackupStatus(7, 10, 900, 1000, TimeSpan.FromSeconds(9))],
            delivered
        );
    }

    [Fact]
    internal void Report_WithDifferentTotals_StartsANewPhaseInsteadOfMerging()
    {
        List<BackupStatus> delivered = [];
        var sut = CreateSut(this.context, delivered.Add);

        sut.Report(new BackupStatus(8, 10, 800, 1000, TimeSpan.FromSeconds(8)));
        sut.Report(new BackupStatus(0, 20, 0, 2000, TimeSpan.Zero));
        this.context.RunAll();

        Assert.Equal<BackupStatus>([new BackupStatus(0, 20, 0, 2000, TimeSpan.Zero)], delivered);
    }

    [Fact]
    internal void Report_AfterADeliveryHasRun_QueuesTheNextOne()
    {
        List<BackupStatus> delivered = [];
        var sut = CreateSut(this.context, delivered.Add);

        sut.Report(new BackupStatus(1, 10, 100, 1000, TimeSpan.FromSeconds(1)));
        this.context.RunAll();

        sut.Report(new BackupStatus(2, 10, 200, 1000, TimeSpan.FromSeconds(2)));
        var queued = this.context.Pending;
        this.context.RunAll();

        Assert.Multiple(
            () => Assert.Equal(1, queued),
            () => Assert.Equal(2, delivered.Count),
            () =>
                Assert.Equal(
                    new BackupStatus(2, 10, 200, 1000, TimeSpan.FromSeconds(2)),
                    delivered[^1]
                )
        );
    }

    [Fact]
    internal async Task Report_WithoutASynchronizationContext_DeliversOnTheThreadPool()
    {
        TaskCompletionSource<BackupStatus> delivered = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var sut = CreateSut(null, status => delivered.TrySetResult(status));

        sut.Report(new BackupStatus(1, 2, 10, 20, TimeSpan.FromSeconds(1)));

        var status = await delivered.Task.WaitAsync(
            TimeSpan.FromSeconds(30),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(new BackupStatus(1, 2, 10, 20, TimeSpan.FromSeconds(1)), status);
    }

    /// <summary>
    /// Builds the sink while <paramref name="capturedContext"/> is the current context, so that is the one it
    /// captures, and puts the test thread's own context back straight away. Leaving a context that holds its
    /// posts installed on the test thread would also hold the test framework's own continuations.
    /// </summary>
    /// <param name="capturedContext">The context the sink should deliver on, or <see langword="null"/> for none.</param>
    /// <param name="handler">The callback that receives the delivered statuses.</param>
    /// <returns>The system under test.</returns>
    private static CoalescingStatusProgress CreateSut(
        SynchronizationContext? capturedContext,
        Action<BackupStatus> handler
    )
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(capturedContext);

        try
        {
            return new CoalescingStatusProgress(handler);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    /// <summary>
    /// A synchronization context that holds posted callbacks until the test runs them, so a test can
    /// observe how many deliveries a burst of reports queues and what each one carries.
    /// </summary>
    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        /// <summary>
        /// The callbacks posted and not run yet, in posting order.
        /// </summary>
        private readonly Queue<(SendOrPostCallback Callback, object? State)> queue = new();

        /// <summary>
        /// Gets the number of posted callbacks waiting to run.
        /// </summary>
        public int Pending => this.queue.Count;

        /// <inheritdoc/>
        public override void Post(SendOrPostCallback d, object? state)
        {
            this.queue.Enqueue((d, state));
        }

        /// <inheritdoc/>
        public override void Send(SendOrPostCallback d, object? state)
        {
            d(state);
        }

        /// <summary>
        /// Runs every queued callback, including any a callback posts while running.
        /// </summary>
        public void RunAll()
        {
            while (this.queue.TryDequeue(out var item))
            {
                item.Callback(item.State);
            }
        }
    }
}
