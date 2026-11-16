using Avalonia.Controls;

using BackupZCrypt.Desktop.Behaviors;
using BackupZCrypt.Test.Common;

namespace BackupZCrypt.Test.Unit.Desktop.Behaviors;

/// <summary>
/// UI tests for <see cref="WindowCloseGuard"/>. Refusing a user close while an operation runs is covered through
/// the operation dialog; these cover a guard with no window to guard and one that has been released.
/// </summary>
public sealed class WindowCloseGuardTests
{
    [Fact]
    internal Task OutsideAWindow_ReturnsNull()
    {
        return HeadlessUi.RunAsync(static () =>
            Assert.Null(WindowCloseGuard.Open(new Border(), static () => true)));
    }

    [Fact]
    internal Task OnceDisposed_NoLongerKeepsTheWindowOpen()
    {
        return HeadlessUi.RunAsync(() =>
        {
            Border content = new();
            var window = HeadlessUi.Show(content);
            var guard = WindowCloseGuard.Open(content, static () => true)!;
            guard.Dispose();

            window.Close();

            Assert.False(window.IsVisible);
        });
    }
}
