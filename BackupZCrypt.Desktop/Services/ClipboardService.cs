using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;

using BackupZCrypt.Desktop.Services.Interfaces;

namespace BackupZCrypt.Desktop.Services;

/// <summary>
/// <see cref="IClipboardService"/> implementation that writes text to the main window's clipboard
/// using Avalonia's data-transfer model.
/// </summary>
internal sealed class ClipboardService : IClipboardService
{
    /// <inheritdoc/>
    /// <remarks>
    /// Does nothing when the application is not running a classic desktop lifetime or its main window exposes
    /// no clipboard; in that case the returned task completes without the clipboard having been updated.
    /// The transfer object is disposed only once the clipboard write has completed, by which point the
    /// clipboard holds its own copy of the payload.
    /// </remarks>
    public async Task SetTextAsync(string text)
    {
        if (
            Avalonia.Application.Current?.ApplicationLifetime
                is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow?.Clipboard is { } clipboard
        )
        {
            using var dataTransfer = new DataTransfer();
            dataTransfer.Add(DataTransferItem.CreateText(text));
            await clipboard.SetDataAsync(dataTransfer);
        }
    }
}
