using Avalonia.Controls;
using Avalonia.Input.Platform;

using BackupZCrypt.Desktop.Platform;
using BackupZCrypt.Test.Common;

namespace BackupZCrypt.Test.Unit.Desktop.Platform;

/// <summary>
/// UI tests for <see cref="ClipboardService"/>: text reaches the clipboard, and a copied secret is wiped once its
/// lifetime has passed unless the user copied something else since, a newer secret replaced it, or the service
/// was disposed.
/// </summary>
public sealed class ClipboardServiceTests
{
    /// <summary>
    /// A secret lifetime short enough for tests to wait out.
    /// </summary>
    private static readonly TimeSpan ShortLifetime = TimeSpan.FromMilliseconds(50);

    [Fact]
    internal Task SetTextAsync_PutsTheTextOnTheClipboardAndLeavesIt()
    {
        return WithClipboardAsync(async clipboard =>
        {
            using ClipboardService sut = new(() => clipboard, ShortLifetime);

            await sut.SetTextAsync("plain text");
            await Task.Delay(ShortLifetime * 4);

            Assert.Equal("plain text", await clipboard.TryGetTextAsync());
        });
    }

    [Fact]
    internal Task SetSensitiveTextAsync_ClearsTheSecretOnceItsLifetimeHasPassed()
    {
        return WithClipboardAsync(async clipboard =>
        {
            using ClipboardService sut = new(() => clipboard, ShortLifetime);

            await sut.SetSensitiveTextAsync("secret");
            var copied = await clipboard.TryGetTextAsync();
            await WaitForTextAsync(clipboard, null);

            Assert.Equal("secret", copied);
        });
    }

    [Fact]
    internal Task SetSensitiveTextAsync_LeavesWhatTheUserCopiedSince()
    {
        return WithClipboardAsync(async clipboard =>
        {
            using ClipboardService sut = new(() => clipboard, ShortLifetime);

            await sut.SetSensitiveTextAsync("secret");
            await sut.SetTextAsync("copied later");
            await Task.Delay(ShortLifetime * 6);

            Assert.Equal("copied later", await clipboard.TryGetTextAsync());
        });
    }

    [Fact]
    internal Task SetSensitiveTextAsync_ANewerSecretRestartsTheLifetimeAndIsClearedInTurn()
    {
        return WithClipboardAsync(async clipboard =>
        {
            using ClipboardService sut = new(() => clipboard, TimeSpan.FromMilliseconds(1000));

            await sut.SetSensitiveTextAsync("first");
            await Task.Delay(TimeSpan.FromMilliseconds(600));
            await sut.SetSensitiveTextAsync("second");
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            var midway = await clipboard.TryGetTextAsync();
            await WaitForTextAsync(clipboard, null);

            Assert.Equal("second", midway);
        });
    }

    [Fact]
    internal Task Dispose_CancelsThePendingClear()
    {
        return WithClipboardAsync(async clipboard =>
        {
            ClipboardService sut = new(() => clipboard, ShortLifetime);

            await sut.SetSensitiveTextAsync("secret");
            sut.Dispose();
            sut.Dispose();
            await Task.Delay(ShortLifetime * 6);

            Assert.Equal("secret", await clipboard.TryGetTextAsync());
        });
    }

    [Fact]
    internal Task WithoutAClipboard_CopyingDoesNothing()
    {
        return HeadlessUi.RunAsync(async () =>
        {
            using ClipboardService withoutWindow = new();
            using ClipboardService withoutClipboard = new(static () => null, ShortLifetime);

            await withoutWindow.SetTextAsync("text");
            await withoutClipboard.SetSensitiveTextAsync("secret");
            await Task.Delay(ShortLifetime * 4);
        });
    }

    /// <summary>
    /// Runs a test against the clipboard of a shown window, emptying it before and after.
    /// </summary>
    /// <param name="test">The test body.</param>
    /// <returns>A task that completes once the test has run on the UI thread.</returns>
    private static Task WithClipboardAsync(Func<IClipboard, Task> test)
    {
        return HeadlessUi.RunAsync(async () =>
        {
            var window = HeadlessUi.Show(new TextBlock());
            var clipboard = window.Clipboard!;
            await clipboard.ClearAsync();

            try
            {
                await test(clipboard);
            }
            finally
            {
                await clipboard.ClearAsync();
                window.Close();
            }
        });
    }

    /// <summary>
    /// Waits until the clipboard holds the expected text.
    /// </summary>
    /// <param name="clipboard">The clipboard.</param>
    /// <param name="expected">The expected text, or <see langword="null"/> for an empty clipboard.</param>
    /// <returns>A task that completes once the clipboard holds it.</returns>
    private static async Task WaitForTextAsync(IClipboard clipboard, string? expected)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);

        while (!string.Equals(await clipboard.TryGetTextAsync(), expected, StringComparison.Ordinal))
        {
            Assert.True(DateTime.UtcNow < deadline, "The clipboard did not reach the expected text in time.");
            await Task.Delay(20);
        }
    }
}
