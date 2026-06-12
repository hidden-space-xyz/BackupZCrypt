using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using BackupZCrypt.Desktop.Services.Interfaces;

namespace BackupZCrypt.Desktop.Services;

internal sealed class FilePickerService : IFilePickerService
{
    public async Task<string?> PickFolderAsync(string title)
    {
        var topLevel = GetTopLevel();
        if (topLevel is null)
        {
            return null;
        }

        var result = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = title, AllowMultiple = false }
        );

        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickFileAsync(string title)
    {
        var topLevel = GetTopLevel();
        if (topLevel is null)
        {
            return null;
        }

        var result = await topLevel.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions { Title = title, AllowMultiple = false }
        );

        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }

    private static TopLevel? GetTopLevel()
    {
        return Avalonia.Application.Current?.ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
    }
}
