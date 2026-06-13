namespace BackupZCrypt.Test.Common;

public sealed class RecordingProgress<T> : IProgress<T>
{
    public List<T> Reports { get; } = [];

    public void Report(T value)
    {
        Reports.Add(value);
    }
}
