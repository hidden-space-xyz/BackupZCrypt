namespace BackupZCrypt.Test.Common;

/// <summary>
/// Progress reporter that records every reported value so tests can assert on progress updates.
/// </summary>
/// <typeparam name="T">The type of the progress value being reported.</typeparam>
public sealed class RecordingProgress<T> : IProgress<T>
{
    /// <summary>
    /// Gets the values captured so far, in the order they were reported.
    /// </summary>
    public List<T> Reports { get; } = [];

    /// <inheritdoc/>
    public void Report(T value)
    {
        Reports.Add(value);
    }
}
