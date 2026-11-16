using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;

using BackupZCrypt.Desktop.Controls;
using BackupZCrypt.Test.Common;

namespace BackupZCrypt.Test.Unit.Desktop.Controls;

/// <summary>
/// UI tests for <see cref="WindowDragArea"/>, the scrim that lets the user move the window while the operation
/// overlay covers its title bar.
/// </summary>
public sealed class WindowDragAreaTests
{
    [Fact]
    internal Task Press_WithEitherButton_LeavesTheWindowAsItWasAndStillRaisesThePress()
    {
        return HeadlessUi.RunAsync(() =>
        {
            WindowDragArea sut = new() { Background = Avalonia.Media.Brushes.Transparent };
            var window = HeadlessUi.Show(sut);
            var presses = 0;
            sut.PointerPressed += (_, _) => presses++;

            try
            {
                Point inside = new(10, 10);
                window.MouseDown(inside, MouseButton.Left);
                window.MouseUp(inside, MouseButton.Left);
                window.MouseDown(inside, MouseButton.Right);
                window.MouseUp(inside, MouseButton.Right);
                Dispatcher.UIThread.RunJobs();

                Assert.Multiple(
                    () => Assert.Equal(2, presses),
                    () => Assert.Equal(WindowState.Normal, window.WindowState),
                    () => Assert.True(window.IsVisible)
                );
            }
            finally
            {
                window.Close();
            }
        });
    }
}
