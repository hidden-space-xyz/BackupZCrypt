using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

using BackupZCrypt.Desktop.Services.Interfaces;

namespace BackupZCrypt.Desktop.Services;

/// <summary>
/// <see cref="IFilePickerService"/> implementation backed by the Avalonia storage provider of the main window.
/// </summary>
internal sealed class FilePickerService : IFilePickerService
{
    /// <inheritdoc/>
    /// <remarks>
    /// Also yields <see langword="null"/> when there is no desktop main window to host the dialog.
    /// </remarks>
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

    /// <summary>
    /// Resolves the main window whose storage provider hosts the folder picker.
    /// </summary>
    /// <returns>The main window, or <see langword="null"/> when the application is not running a classic desktop lifetime.</returns>
    private static Window? GetTopLevel()
    {
        return Avalonia.Application.Current?.ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
    }
}
