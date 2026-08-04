using System.Diagnostics.CodeAnalysis;

namespace BackupZCrypt.Domain.Services.Interfaces;

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
    public static abstract TSelf DefaultValue { get; }

    /// <summary>
    /// Gets the file name (without directory) under which this settings type is stored.
    /// </summary>
    [SuppressMessage(
        "Major Code Smell",
        "S2743:Static fields should not be used in generic types",
        Justification = "A static abstract interface member resolves per closed type by design: that is "
            + "precisely what gives every settings type its own file name. S2743 targets shared mutable "
            + "static state in generic classes, which this is not."
    )]
    public static abstract string FileName { get; }
}
