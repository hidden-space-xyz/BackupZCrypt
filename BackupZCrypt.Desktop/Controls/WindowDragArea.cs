using Avalonia.Controls;
using Avalonia.Input;

namespace BackupZCrypt.Desktop.Controls;

/// <summary>
/// A border that moves the window it is in when the user drags it with the left mouse button, for surfaces
/// that cover the title bar.
/// </summary>
internal sealed class WindowDragArea : Border
{
    /// <summary>
    /// Gets the type whose styles the control uses: those of a regular border.
    /// </summary>
    protected override Type StyleKeyOverride => typeof(Border);

    /// <inheritdoc/>
    /// <remarks>
    /// Starts moving the hosting window when the left button goes down, and does nothing outside a window.
    /// </remarks>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        base.OnPointerPressed(e);

        if (
            TopLevel.GetTopLevel(this) is Window window
            && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
        )
        {
            window.BeginMoveDrag(e);
        }
    }
}
