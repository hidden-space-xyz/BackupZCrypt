namespace BackupZCrypt.Test.Common;

/// <summary>
/// Progress reporter that records every reported value so tests can assert on progress updates.
/// </summary>
public sealed class RecordingProgress<T> : IProgress<T>
{
    public List<T> Reports { get; } = [];

    public void Report(T value)
    {
        Reports.Add(value);
    }
}
