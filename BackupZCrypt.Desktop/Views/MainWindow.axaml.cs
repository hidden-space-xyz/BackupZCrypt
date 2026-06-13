using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Path = Avalonia.Controls.Shapes.Path;

namespace BackupZCrypt.Desktop.Views;

public sealed partial class MainWindow : Window
{
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

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == WindowStateProperty)
        {
            var maximized = WindowState == WindowState.Maximized;

            // The extended client area overflows the screen edges when maximized;
            // pad the content back in by the off-screen margin.
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
