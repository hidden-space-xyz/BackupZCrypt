using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using Path = Avalonia.Controls.Shapes.Path;

namespace BackupZCrypt.Desktop.Views;

/// <summary>
/// The application's main window, hosting the navigation shell and a custom title bar.
/// </summary>
internal sealed partial class MainWindow : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Starts a window move drag when the custom title bar is pressed with the left button.
    /// </summary>
    /// <param name="sender">The title-bar element that raised the event.</param>
    /// <param name="e">The pointer event carrying the button state.</param>
    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    /// <summary>
    /// Maximizes or restores the window when the custom title bar is double-tapped.
    /// </summary>
    /// <param name="sender">The title-bar element that raised the event.</param>
    /// <param name="e">The tap event data.</param>
    private void OnTitleBarDoubleTapped(object? sender, TappedEventArgs e)
    {
        ToggleMaximize();
    }

    /// <summary>
    /// Minimizes the window from the title-bar minimize button.
    /// </summary>
    /// <param name="sender">The button that raised the event.</param>
    /// <param name="e">The click event data.</param>
    private void OnMinimize(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    /// <summary>
    /// Maximizes or restores the window from the title-bar maximize button.
    /// </summary>
    /// <param name="sender">The button that raised the event.</param>
    /// <param name="e">The click event data.</param>
    private void OnMaximizeRestore(object? sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    /// <summary>
    /// Closes the window from the title-bar close button.
    /// </summary>
    /// <param name="sender">The button that raised the event.</param>
    /// <param name="e">The click event data.</param>
    private void OnClose(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>
    /// Switches the window between the maximized and normal states.
    /// </summary>
    private void ToggleMaximize()
    {
        WindowState =
            WindowState is WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    /// <summary>
    /// Reacts to window-state changes, padding the content in when maximized and swapping the
    /// maximize/restore title-bar glyphs.
    /// </summary>
    /// <param name="change">The property change notification.</param>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);

        base.OnPropertyChanged(change);

        if (change.Property == WindowStateProperty)
        {
            var maximized = WindowState is WindowState.Maximized;

            Padding = maximized ? OffScreenMargin : default;

            if (this.FindControl<Path>("MaximizeGlyph") is { } maximizeGlyph)
            {
                maximizeGlyph.IsVisible = !maximized;
            }

            if (this.FindControl<Path>("RestoreGlyph") is { } restoreGlyph)
            {
                restoreGlyph.IsVisible = maximized;
            }
        }
    }
}
