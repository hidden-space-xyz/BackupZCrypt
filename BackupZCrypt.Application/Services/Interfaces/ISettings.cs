namespace BackupZCrypt.Application.Services.Interfaces;

/// <summary>
/// Marks a settings type that can be persisted and recreated with defaults, using the
/// curiously recurring template pattern so static members resolve to the concrete type.
/// </summary>
/// <typeparam name="TSelf">The concrete settings type implementing this interface.</typeparam>
public interface ISettings<TSelf>
    where TSelf : class, ISettings<TSelf>
{
    /// <summary>
    /// Gets the default instance used when no persisted settings exist or the stored file is corrupted.
    /// </summary>
    public abstract static TSelf DefaultValue { get; }

    /// <summary>
    /// Gets the file name (without directory) under which this settings type is stored.
    /// </summary>
    public abstract static string FileName { get; }
}
