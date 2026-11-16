using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;

using BackupZCrypt.Desktop.Behaviors;
using BackupZCrypt.Test.Common;

namespace BackupZCrypt.Test.Unit.Desktop.Behaviors;

/// <summary>
/// UI tests for <see cref="ModalInputScope"/>: while it is open, input aimed outside the overlay is swallowed and
/// focus is pulled back, input inside the overlay works normally, and focus goes back where it was only if that
/// element can still take it.
/// </summary>
public sealed class ModalInputScopeTests
{
    [Fact]
    internal Task Open_OnAnOverlayOutsideAWindow_ReturnsNull()
    {
        return HeadlessUi.RunAsync(static () =>
            Assert.Null(ModalInputScope.Open(new Border(), static () => { })));
    }

    [Fact]
    internal Task InputInsideTheOverlay_ReachesItsControls()
    {
        return WithPageAndOverlayAsync(static (window, page, overlay) =>
        {
            var refocused = 0;
            using var scope = ModalInputScope.Open(overlay, () => refocused++)!;

            overlay.Focus();
            window.KeyPress(Key.B, RawInputModifiers.None, PhysicalKey.B, "b");
            window.KeyTextInput("b");
            Dispatcher.UIThread.RunJobs();

            Assert.Multiple(
                () => Assert.Equal("b", overlay.Text),
                () => Assert.Equal(0, refocused)
            );
        });
    }

    [Fact]
    internal Task InputAimedAtThePage_IsSwallowedAndFocusIsPulledBack()
    {
        return WithPageAndOverlayAsync(static (window, page, overlay) =>
        {
            var refocused = 0;
            using var scope = ModalInputScope.Open(overlay, () => refocused++)!;

            page.Focus();
            window.KeyPress(Key.C, RawInputModifiers.None, PhysicalKey.C, "c");
            window.KeyTextInput("c");
            Dispatcher.UIThread.RunJobs();

            Assert.Multiple(
                () => Assert.True(string.IsNullOrEmpty(page.Text)),
                () => Assert.Equal(1, refocused)
            );
        });
    }

    [Fact]
    internal Task Dispose_GivesFocusBackOnlyToAnElementThatCanStillTakeIt()
    {
        return WithPageAndOverlayAsync(static (window, page, overlay) =>
        {
            var withoutFocus = ModalInputScope.Open(overlay, static () => { })!;
            withoutFocus.Dispose();

            page.Focus();
            var restoring = ModalInputScope.Open(overlay, static () => { })!;
            overlay.Focus();
            restoring.Dispose();
            var restored = page.IsFocused;

            page.Focus();
            var notFocusable = ModalInputScope.Open(overlay, static () => { })!;
            overlay.Focus();
            page.Focusable = false;
            notFocusable.Dispose();
            page.Focusable = true;

            page.Focus();
            var removed = ModalInputScope.Open(overlay, static () => { })!;
            overlay.Focus();
            ((Panel)window.Content!).Children.Remove(page);
            removed.Dispose();

            Assert.Multiple(
                () => Assert.True(restored),
                () => Assert.True(overlay.IsFocused)
            );
        });
    }

    /// <summary>
    /// Shows a window with a text box standing for the page and another standing for the overlay.
    /// </summary>
    /// <param name="test">The test body, given the window, the page box, and the overlay box.</param>
    /// <returns>A task that completes once the test has run on the UI thread.</returns>
    private static Task WithPageAndOverlayAsync(Action<Window, TextBox, TextBox> test)
    {
        return HeadlessUi.RunAsync(() =>
        {
            TextBox page = new();
            TextBox overlay = new();
            var window = HeadlessUi.Show(new StackPanel { Children = { page, overlay } });

            try
            {
                test(window, page, overlay);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
