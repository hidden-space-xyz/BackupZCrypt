using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;

using BackupZCrypt.Desktop.ViewModels;

namespace BackupZCrypt.Desktop.Platform;

/// <summary>
/// <see cref="IClipboardService"/> implementation that writes text to the main window's clipboard
/// using Avalonia's data-transfer model.
/// </summary>
internal sealed class ClipboardService : IClipboardService, IDisposable
{
    /// <summary>
    /// How long a copied secret stays on the clipboard before it is cleared, unless a test shortens it.
    /// </summary>
    private static readonly TimeSpan DefaultSensitiveTextLifetime = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a copied secret stays on the clipboard before it is cleared.
    /// </summary>
    private readonly TimeSpan sensitiveTextLifetime;

    /// <summary>
    /// The pending clear of the last secret copied, cancelled when another secret is copied.
    /// </summary>
    private CancellationTokenSource? pendingClear;

    /// <summary>
    /// Resolves the clipboard to write to, or <see langword="null"/> when there is none.
    /// </summary>
    private readonly Func<IClipboard?> getClipboard;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClipboardService"/> class that writes to the main window's
    /// clipboard and clears a copied secret after thirty seconds.
    /// </summary>
    public ClipboardService()
        : this(GetMainWindowClipboard, DefaultSensitiveTextLifetime) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="ClipboardService"/> class with the given clipboard and
    /// secret lifetime.
    /// </summary>
    /// <param name="getClipboard">Resolves the clipboard to write to, or <see langword="null"/> when there is none.</param>
    /// <param name="sensitiveTextLifetime">How long a copied secret stays on the clipboard.</param>
    internal ClipboardService(Func<IClipboard?> getClipboard, TimeSpan sensitiveTextLifetime)
    {
        this.getClipboard = getClipboard;
        this.sensitiveTextLifetime = sensitiveTextLifetime;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Does nothing when no clipboard can be resolved (with the default constructor, when the application is
    /// not running a classic desktop lifetime or its main window exposes no clipboard); in that case the
    /// returned task completes without the clipboard having been updated.
    /// The transfer object is disposed only once the clipboard write has completed, by which point the
    /// clipboard holds its own copy of the payload.
    /// </remarks>
    public async Task SetTextAsync(string text)
    {
        if (this.getClipboard() is { } clipboard)
        {
            using var dataTransfer = new DataTransfer();
            dataTransfer.Add(DataTransferItem.CreateText(text));
            await clipboard.SetDataAsync(dataTransfer);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Clipboard managers and other programs can read whatever stays on the clipboard, so a copied
    /// password is cleared after the secret lifetime, but only if the clipboard still
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
    private async Task ClearLaterAsync(string text, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(this.sensitiveTextLifetime, cancellationToken);

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (
                    this.getClipboard() is { } clipboard
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
    private static IClipboard? GetMainWindowClipboard()
    {
        return
            Avalonia.Application.Current?.ApplicationLifetime
                is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow?.Clipboard
            : null;
    }
}
