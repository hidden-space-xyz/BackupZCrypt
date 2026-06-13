using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Path = Avalonia.Controls.Shapes.Path;

namespace BackupZCrypt.Desktop.Views;

/// <summary>
/// The application's main window, hosting the navigation shell and a custom title bar.
/// </summary>
public sealed partial class MainWindow : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnTitleBarDoubleTapped(object? sender, TappedEventArgs e)
    {
        ToggleMaximize();
    }

    private void OnMinimize(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnMaximizeRestore(object? sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleMaximize()
    {
        WindowState =
            WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    /// <summary>
    /// Reacts to window-state changes, padding the content in when maximized and swapping the
    /// maximize/restore title-bar glyphs.
    /// </summary>
    /// <param name="change">The property change notification.</param>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == WindowStateProperty)
        {
            var maximized = WindowState == WindowState.Maximized;

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
