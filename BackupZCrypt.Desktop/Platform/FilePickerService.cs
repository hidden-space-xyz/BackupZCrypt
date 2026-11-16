using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

using BackupZCrypt.Desktop.ViewModels;

namespace BackupZCrypt.Desktop.Platform;

/// <summary>
/// <see cref="IFilePickerService"/> implementation backed by the Avalonia storage provider of the main window.
/// </summary>
internal sealed class FilePickerService : IFilePickerService
{
    /// <summary>
    /// Resolves the window hosting the folder picker, or <see langword="null"/> when there is none.
    /// </summary>
    private readonly Func<TopLevel?> getTopLevel;

    /// <summary>
    /// Initializes a new instance of the <see cref="FilePickerService"/> class hosted by the main window.
    /// </summary>
    public FilePickerService()
        : this(GetMainWindow) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="FilePickerService"/> class hosted by the given window.
    /// </summary>
    /// <param name="getTopLevel">Resolves the window hosting the folder picker, or <see langword="null"/> when there is none.</param>
    internal FilePickerService(Func<TopLevel?> getTopLevel)
    {
        this.getTopLevel = getTopLevel;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Also yields <see langword="null"/> when there is no desktop main window to host the dialog.
    /// </remarks>
    public async Task<string?> PickFolderAsync(string title)
    {
        var topLevel = this.getTopLevel();
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
    private static Window? GetMainWindow()
    {
        return Avalonia.Application.Current?.ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
    }
}
