using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using BackupZCrypt.Desktop.Services.Interfaces;

namespace BackupZCrypt.Desktop.Services;

internal sealed class ClipboardService : IClipboardService
{
    public async Task SetTextAsync(string text)
    {
        if (
            Avalonia.Application.Current?.ApplicationLifetime
                is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow?.Clipboard is { } clipboard
        )
        {
            // Avalonia 12 replaced IClipboard.SetTextAsync with a data-transfer model.
            var dataTransfer = new DataTransfer();
            dataTransfer.Add(DataTransferItem.CreateText(text));
            await clipboard.SetDataAsync(dataTransfer);
        }
    }
}
