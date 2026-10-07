using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;

using BackupZCrypt.Desktop.Services.Interfaces;

namespace BackupZCrypt.Desktop.Services;

/// <summary>
/// <see cref="IClipboardService"/> implementation that writes text to the main window's clipboard
/// using Avalonia's data-transfer model.
/// </summary>
internal sealed class ClipboardService : IClipboardService, IDisposable
{
    /// <summary>
    /// How long a copied secret stays on the clipboard before it is cleared.
    /// </summary>
    private static readonly TimeSpan SensitiveTextLifetime = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The pending clear of the last secret copied, cancelled when another secret is copied.
    /// </summary>
    private CancellationTokenSource? pendingClear;

    /// <inheritdoc/>
    /// <remarks>
    /// Does nothing when the application is not running a classic desktop lifetime or its main window exposes
    /// no clipboard; in that case the returned task completes without the clipboard having been updated.
    /// The transfer object is disposed only once the clipboard write has completed, by which point the
    /// clipboard holds its own copy of the payload.
    /// </remarks>
    public async Task SetTextAsync(string text)
    {
        if (GetClipboard() is { } clipboard)
        {
            using var dataTransfer = new DataTransfer();
            dataTransfer.Add(DataTransferItem.CreateText(text));
            await clipboard.SetDataAsync(dataTransfer);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Clipboard managers and other programs can read whatever stays on the clipboard, so a copied
    /// password is cleared after <see cref="SensitiveTextLifetime"/>, but only if the clipboard still
    /// holds it: whatever the user copied since is left alone.
    /// </remarks>
    public async Task SetSensitiveTextAsync(string text)
    {
        await this.SetTextAsync(text);

        if (this.pendingClear is not null)
        {
            await this.pendingClear.CancelAsync();
            this.pendingClear.Dispose();
        }

        this.pendingClear = new CancellationTokenSource();
        _ = ClearLaterAsync(text, this.pendingClear.Token);
    }

    /// <summary>
    /// Cancels the pending clear of the last secret copied.
    /// </summary>
    public void Dispose()
    {
        this.pendingClear?.Cancel();
        this.pendingClear?.Dispose();
        this.pendingClear = null;
    }

    /// <summary>
    /// Clears the clipboard once the secret's lifetime has passed, if it still holds that secret.
    /// </summary>
    /// <param name="text">The secret that was copied.</param>
    /// <param name="cancellationToken">A token cancelled when another secret is copied.</param>
    /// <returns>A task that completes once the clipboard has been checked.</returns>
    private static async Task ClearLaterAsync(string text, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(SensitiveTextLifetime, cancellationToken);

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (
                    GetClipboard() is { } clipboard
                    && string.Equals(await clipboard.TryGetTextAsync(), text, StringComparison.Ordinal)
                )
                {
                    await clipboard.ClearAsync();
                }
            });
        }
        catch (OperationCanceledException)
        {
            return;
        }
    }

    /// <summary>
    /// Returns the clipboard of the main window, if the application has one.
    /// </summary>
    /// <returns>The clipboard, or <see langword="null"/>.</returns>
    private static IClipboard? GetClipboard()
    {
        return
            Avalonia.Application.Current?.ApplicationLifetime
                is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow?.Clipboard
            : null;
    }
}
