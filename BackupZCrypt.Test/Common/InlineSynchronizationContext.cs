namespace BackupZCrypt.Test.Common;

/// <summary>
/// A synchronization context that runs every posted or sent callback inline on the calling thread,
/// so view models that marshal progress and continuations through the captured context deliver them
/// synchronously in tests.
/// </summary>
internal sealed class InlineSynchronizationContext : SynchronizationContext
{
    /// <inheritdoc/>
    public override void Post(SendOrPostCallback d, object? state)
    {
        d(state);
    }

    /// <inheritdoc/>
    public override void Send(SendOrPostCallback d, object? state)
    {
        d(state);
    }
}
