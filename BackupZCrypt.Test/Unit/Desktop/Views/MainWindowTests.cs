using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;

using BackupZCrypt.Desktop.Resources;
using BackupZCrypt.Desktop.ViewModels;
using BackupZCrypt.Desktop.Views;
using BackupZCrypt.Test.Common;

using Microsoft.Extensions.DependencyInjection;

using Path = Avalonia.Controls.Shapes.Path;

namespace BackupZCrypt.Test.Unit.Desktop.Views;

/// <summary>
/// UI tests for <see cref="MainWindow"/>: the sidebar shows the page of every entry, and the custom title bar
/// minimizes, maximizes, restores, and closes the window and swaps its maximize glyph.
/// </summary>
public sealed class MainWindowTests
{
    [Fact]
    internal Task SelectingEachEntry_ShowsItsPageInTheContentArea()
    {
        return HeadlessUi.RunAsync(async () =>
        {
            using var services = DesktopShell.CreateServices();
            var shell = services.GetRequiredService<MainWindowViewModel>();
            var window = Show(shell);

            try
            {
                Dictionary<Type, Type> expected = new()
                {
                    [typeof(CreateBackupViewModel)] = typeof(CreateBackupView),
                    [typeof(UpdateBackupViewModel)] = typeof(UpdateBackupView),
                    [typeof(RestoreBackupViewModel)] = typeof(RestoreBackupView),
                    [typeof(VerifyBackupViewModel)] = typeof(VerifyBackupView),
                    [typeof(SettingsViewModel)] = typeof(SettingsView),
                    [typeof(AboutViewModel)] = typeof(AboutView),
                };

                foreach (var item in shell.OperationItems.Concat(shell.UtilityItems))
                {
                    if (shell.OperationItems.Contains(item))
                    {
                        shell.SelectedOperation = item;
                    }
                    else
                    {
                        shell.SelectedUtility = item;
                    }

                    var viewType = expected[item.Page.GetType()];
                    await HeadlessUi.WaitUntilAsync(() => ShownPage(window)?.GetType() == viewType);
                    Assert.Same(item.Page, ShownPage(window)!.DataContext);
                }
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    internal Task TitleBarButtons_MinimizeMaximizeRestoreAndClose()
    {
        return HeadlessUi.RunAsync(() =>
        {
            using var services = DesktopShell.CreateServices();
            var window = Show(services.GetRequiredService<MainWindowViewModel>());
            var closed = false;
            window.Closed += (_, _) => closed = true;

            Click(window, Strings.WindowMaximize);
            var maximized = (window.WindowState, Glyphs(window));

            Click(window, Strings.WindowMaximize);
            var restored = (window.WindowState, window.Padding, Glyphs(window));

            Click(window, Strings.WindowMinimize);
            var minimized = window.WindowState;

            Click(window, Strings.WindowClose);

            Assert.Multiple(
                () => Assert.Equal(WindowState.Maximized, maximized.WindowState),
                () => Assert.Equal((false, true), maximized.Item2),
                () => Assert.Equal(WindowState.Normal, restored.WindowState),
                () => Assert.Equal(default, restored.Padding),
                () => Assert.Equal((true, false), restored.Item3),
                () => Assert.Equal(WindowState.Minimized, minimized),
                () => Assert.True(closed)
            );
        });
    }

    [Fact]
    internal Task TitleBar_DoubleTapTogglesMaximizeAndRightClickLeavesTheWindowAlone()
    {
        return HeadlessUi.RunAsync(() =>
        {
            using var services = DesktopShell.CreateServices();
            var window = Show(services.GetRequiredService<MainWindowViewModel>());

            try
            {
                Point titleBar = new(60, 30);
                window.MouseDown(titleBar, MouseButton.Right);
                window.MouseUp(titleBar, MouseButton.Right);
                var afterRightClick = window.WindowState;

                DoubleClick(window, titleBar);
                var afterDoubleTap = window.WindowState;

                Assert.Multiple(
                    () => Assert.Equal(WindowState.Normal, afterRightClick),
                    () => Assert.Equal(WindowState.Maximized, afterDoubleTap)
                );
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>
    /// Shows the main window for a shell.
    /// </summary>
    /// <param name="shell">The shell to bind.</param>
    /// <returns>The shown window.</returns>
    private static MainWindow Show(MainWindowViewModel shell)
    {
        MainWindow window = new() { DataContext = shell };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    /// <summary>
    /// Clicks a title-bar button.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="automationName">The button's accessible name.</param>
    private static void Click(MainWindow window, string automationName)
    {
        DesktopShell.FindButton(window, automationName).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Double-clicks with the left button.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="point">Where to click.</param>
    private static void DoubleClick(MainWindow window, Point point)
    {
        for (var i = 0; i < 2; i++)
        {
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
        }

        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Reads which of the maximize and restore glyphs are visible.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <returns>The visibility of the maximize glyph and of the restore glyph.</returns>
    private static (bool Maximize, bool Restore) Glyphs(MainWindow window)
    {
        return (
            window.FindControl<Path>("MaximizeGlyph")!.IsVisible,
            window.FindControl<Path>("RestoreGlyph")!.IsVisible
        );
    }

    /// <summary>
    /// Finds the page view currently visible in the content area.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <returns>The visible page view, or <see langword="null"/> while none is shown.</returns>
    private static UserControl? ShownPage(MainWindow window)
    {
        return window.GetLogicalDescendants()
            .OfType<UserControl>()
            .FirstOrDefault(view => view is not OperationStatusView && view.IsEffectivelyVisible && view.DataContext is ViewModelBase { IsActivePage: true });
    }
}
