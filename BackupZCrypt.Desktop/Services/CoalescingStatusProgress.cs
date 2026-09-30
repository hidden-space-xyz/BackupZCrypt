using BackupZCrypt.Domain.ValueObjects.Backup;

namespace BackupZCrypt.Desktop.Services;

/// <summary>
/// Delivers engine status reports to the synchronization context it was created on, never more than one at a
/// time and never going backwards.
/// </summary>
/// <remarks>
/// <para>
/// The engine reports once per file from several worker threads at once. A plain <see cref="Progress{T}"/>
/// queues one callback per file, which floods the UI thread on a folder of small files, and it delivers them
/// in whatever order the threads happened to post: a later callback can carry fewer processed bytes than an
/// earlier one, so the progress bar and the file counter jump backwards and forwards.
/// </para>
/// <para>
/// This sink keeps a single delivery queued at any time and hands it the furthest status seen so far, merging
/// every report of the same phase by taking the largest processed counts and elapsed time. A report with
/// different totals starts a new phase and replaces the status outright.
/// </para>
/// </remarks>
internal sealed class CoalescingStatusProgress : IProgress<BackupStatus>
{
    /// <summary>
    /// Guards <see cref="latest"/> and <see cref="deliveryQueued"/>, which the reporting threads and the
    /// delivering thread share.
    /// </summary>
    private readonly Lock gate = new();

    /// <summary>
    /// The callback that applies a status, invoked on <see cref="context"/>.
    /// </summary>
    private readonly Action<BackupStatus> handler;

    /// <summary>
    /// The context captured at construction, or <see langword="null"/> to deliver on the thread pool as
    /// <see cref="Progress{T}"/> does.
    /// </summary>
    private readonly SynchronizationContext? context;

    /// <summary>
    /// The furthest status reported and not yet delivered, or the last one delivered.
    /// </summary>
    private BackupStatus? latest;

    /// <summary>
    /// A value indicating whether a delivery is queued and will pick up <see cref="latest"/>.
    /// </summary>
    private bool deliveryQueued;

    /// <summary>
    /// Initializes a new instance of the <see cref="CoalescingStatusProgress"/> class, capturing the current
    /// synchronization context as the one reports are delivered on.
    /// </summary>
    /// <param name="handler">The callback that applies a status.</param>
    public CoalescingStatusProgress(Action<BackupStatus> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        this.handler = handler;
        context = SynchronizationContext.Current;
    }

    /// <summary>
    /// Merges a report into the pending status and queues a delivery unless one is already on its way.
    /// </summary>
    /// <param name="value">The status reported by the engine.</param>
    public void Report(BackupStatus value)
    {
        ArgumentNullException.ThrowIfNull(value);

        lock (gate)
        {
            latest = Furthest(latest, value);

            if (deliveryQueued)
            {
                return;
            }

            deliveryQueued = true;
        }

        if (context is null)
        {
            _ = ThreadPool.QueueUserWorkItem(static sink => sink.Deliver(), this, preferLocal: false);
        }
        else
        {
            context.Post(static sink => ((CoalescingStatusProgress)sink!).Deliver(), this);
        }
    }

    /// <summary>
    /// Merges two reports of the same phase into the furthest progress either of them shows.
    /// </summary>
    /// <param name="current">The pending status, or <see langword="null"/> before the first report.</param>
    /// <param name="next">The status just reported.</param>
    /// <returns>
    /// <paramref name="next"/> when it starts a new phase; otherwise a status holding the largest processed
    /// counts and elapsed time of both.
    /// </returns>
    internal static BackupStatus Furthest(BackupStatus? current, BackupStatus next)
    {
        ArgumentNullException.ThrowIfNull(next);

        if (
            current is null
            || current.TotalFiles != next.TotalFiles
            || current.TotalBytes != next.TotalBytes
        )
        {
            return next;
        }

        return new BackupStatus(
            Math.Max(current.ProcessedFiles, next.ProcessedFiles),
            next.TotalFiles,
            Math.Max(current.ProcessedBytes, next.ProcessedBytes),
            next.TotalBytes,
            current.Elapsed > next.Elapsed ? current.Elapsed : next.Elapsed
        );
    }

    /// <summary>
    /// Hands the pending status to the handler and lets the next report queue a new delivery.
    /// </summary>
    private void Deliver()
    {
        BackupStatus status;

        lock (gate)
        {
            status = latest!;
            deliveryQueued = false;
        }

        handler(status);
    }
}
