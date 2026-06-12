using Avalonia.Controls.ApplicationLifetimes;
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
            await clipboard.SetTextAsync(text);
        }
    }
}
