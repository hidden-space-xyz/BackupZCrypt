using Avalonia;
using Avalonia.Controls;

namespace BackupZCrypt.Desktop.Behaviors;

/// <summary>
/// Keeps a window open while a condition holds, refusing only the closes the user asks for through the
/// window itself. Programmatic closes and system shutdown are never blocked.
/// </summary>
/// <remarks>
/// A modal dialog window guaranteed this by disabling its owner. An in-window overlay does not, so closing
/// the application mid-operation would abandon the operation halfway; the user has to cancel it first.
/// </remarks>
internal sealed class WindowCloseGuard : IDisposable
{
    /// <summary>
    /// The window being kept open.
    /// </summary>
    private readonly Window window;

    /// <summary>
    /// Returns whether a user-initiated close must currently be refused.
    /// </summary>
    private readonly Func<bool> isBlocking;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowCloseGuard"/> class and starts guarding the window.
    /// </summary>
    /// <param name="window">The window to keep open.</param>
    /// <param name="isBlocking">Returns whether a user-initiated close must currently be refused.</param>
    private WindowCloseGuard(Window window, Func<bool> isBlocking)
    {
        this.window = window;
        this.isBlocking = isBlocking;
        window.Closing += OnClosing;
    }

    /// <summary>
    /// Starts guarding the window a visual is in.
    /// </summary>
    /// <param name="visual">A visual inside the window to keep open.</param>
    /// <param name="isBlocking">Returns whether a user-initiated close must currently be refused.</param>
    /// <returns>The active guard, or <see langword="null"/> when the visual is not in a window.</returns>
    public static WindowCloseGuard? Open(Visual visual, Func<bool> isBlocking)
    {
        ArgumentNullException.ThrowIfNull(visual);
        ArgumentNullException.ThrowIfNull(isBlocking);

        return TopLevel.GetTopLevel(visual) is Window window
            ? new WindowCloseGuard(window, isBlocking)
            : null;
    }

    /// <summary>
    /// Stops guarding the window.
    /// </summary>
    public void Dispose()
    {
        window.Closing -= OnClosing;
    }

    /// <summary>
    /// Cancels a close the user asked for through the window while the condition holds.
    /// </summary>
    /// <param name="sender">The window being closed.</param>
    /// <param name="e">The closing event, which can be cancelled.</param>
    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (
            e.CloseReason is WindowCloseReason.WindowClosing
            && !e.IsProgrammatic
            && isBlocking()
        )
        {
            e.Cancel = true;
        }
    }
}
