namespace BackupZCrypt.Test.Common;

// Synchronous IProgress capture (unlike Progress<T>, which marshals to a captured
// SynchronizationContext and may run the callback after the await completes).
public sealed class RecordingProgress<T> : IProgress<T>
{
    public List<T> Reports { get; } = [];

    public void Report(T value)
    {
        Reports.Add(value);
    }
}
