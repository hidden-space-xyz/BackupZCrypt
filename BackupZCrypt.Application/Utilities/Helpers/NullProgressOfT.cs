namespace BackupZCrypt.Application.Utilities.Helpers;

/// <summary>
/// A progress sink that discards every report, substituted when a caller supplies no sink so the
/// engine can report unconditionally.
/// </summary>
/// <typeparam name="T">The type of progress value being discarded.</typeparam>
internal sealed class NullProgress<T> : IProgress<T>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NullProgress{T}"/> class. Private because the
    /// type is stateless and <see cref="Instance"/> is the only instance anyone needs.
    /// </summary>
    private NullProgress()
    {
    }

    /// <summary>
    /// Gets the shared instance.
    /// </summary>
    public static NullProgress<T> Instance { get; } = new();

    /// <summary>
    /// Discards the report.
    /// </summary>
    /// <param name="value">The reported value, ignored.</param>
    public void Report(T value)
    {
    }
}
