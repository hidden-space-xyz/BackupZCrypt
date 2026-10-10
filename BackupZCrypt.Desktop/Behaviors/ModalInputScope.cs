using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace BackupZCrypt.Desktop.Behaviors;

/// <summary>
/// Keeps keyboard input inside an in-window modal overlay while the scope is open: key presses and text
/// aimed at anything outside the overlay are swallowed, and the element that had keyboard focus before the
/// overlay opened gets it back when the scope is disposed.
/// </summary>
/// <remarks>
/// An overlay drawn in the application's own surface does not disable the page underneath the way a separate
/// modal window disables its owner. Without this scope the page's shortcuts and default buttons, and a text
/// box of the page that still held focus, would keep reacting while the overlay is open.
/// </remarks>
internal sealed class ModalInputScope : IDisposable
{
    /// <summary>
    /// The overlay whose own input is let through.
    /// </summary>
    private readonly Visual overlay;

    /// <summary>
    /// The top level hosting the overlay, where the input aimed at the page underneath is intercepted.
    /// </summary>
    private readonly TopLevel topLevel;

    /// <summary>
    /// Moves keyboard focus back into the overlay after a key press aimed elsewhere was swallowed.
    /// </summary>
    private readonly Action refocus;

    /// <summary>
    /// The element that had keyboard focus when the scope opened, or <see langword="null"/> once it has been
    /// handed back.
    /// </summary>
    private IInputElement? previousFocus;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModalInputScope"/> class, remembering the focused element
    /// and starting to intercept input.
    /// </summary>
    /// <param name="overlay">The overlay whose own input is let through.</param>
    /// <param name="topLevel">The top level hosting the overlay.</param>
    /// <param name="refocus">Moves keyboard focus back into the overlay.</param>
    private ModalInputScope(Visual overlay, TopLevel topLevel, Action refocus)
    {
        this.overlay = overlay;
        this.topLevel = topLevel;
        this.refocus = refocus;

        previousFocus = topLevel.FocusManager?.GetFocusedElement();
        topLevel.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        topLevel.AddHandler(InputElement.TextInputEvent, OnTextInput, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Opens a scope for an overlay that is attached to a top level.
    /// </summary>
    /// <param name="overlay">The overlay whose own input is let through.</param>
    /// <param name="refocus">Moves keyboard focus back into the overlay after a key press aimed elsewhere.</param>
    /// <returns>The open scope, or <see langword="null"/> when the overlay is not attached to a top level.</returns>
    public static ModalInputScope? Open(Visual overlay, Action refocus)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(refocus);

        return TopLevel.GetTopLevel(overlay) is { } topLevel
            ? new ModalInputScope(overlay, topLevel, refocus)
            : null;
    }

    /// <summary>
    /// Stops intercepting input and hands keyboard focus back to the element that had it, when that element
    /// is still in a window and can take focus.
    /// </summary>
    public void Dispose()
    {
        topLevel.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        topLevel.RemoveHandler(InputElement.TextInputEvent, OnTextInput);

        if (
            previousFocus is Visual previous
            && TopLevel.GetTopLevel(previous) is not null
            && previousFocus.Focusable
        )
        {
            _ = previousFocus.Focus();
        }

        previousFocus = null;
    }

    /// <summary>
    /// Returns whether an input event originated from the overlay itself or one of its descendants.
    /// </summary>
    /// <param name="source">The event source.</param>
    /// <returns><see langword="true"/> if the source belongs to the overlay; otherwise <see langword="false"/>.</returns>
    private bool IsFromOverlay(object? source)
    {
        return source is Visual visual
            && (ReferenceEquals(visual, overlay) || overlay.IsVisualAncestorOf(visual));
    }

    /// <summary>
    /// Swallows key presses aimed at anything outside the overlay and pulls focus back into it, so neither
    /// shortcuts nor default buttons of the page underneath can react.
    /// </summary>
    /// <param name="sender">The top level.</param>
    /// <param name="e">The key event, seen during its tunnelling phase.</param>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (IsFromOverlay(e.Source))
        {
            return;
        }

        e.Handled = true;
        refocus();
    }

    /// <summary>
    /// Swallows text typed into anything outside the overlay, such as a text box of the page underneath that
    /// still held focus.
    /// </summary>
    /// <param name="sender">The top level.</param>
    /// <param name="e">The text input event, seen during its tunnelling phase.</param>
    private void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (!IsFromOverlay(e.Source))
        {
            e.Handled = true;
        }
    }
}
