namespace BackupZCrypt.Desktop.Services.Interfaces;

/// <summary>
/// Presents native folder and file pickers and returns the selected local path.
/// </summary>
public interface IFilePickerService
{
    /// <summary>
    /// Prompts the user to choose a single folder.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <returns>The selected folder's local path, or <see langword="null"/> if the dialog was cancelled.</returns>
    Task<string?> PickFolderAsync(string title);

    /// <summary>
    /// Prompts the user to choose a single file.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <returns>The selected file's local path, or <see langword="null"/> if the dialog was cancelled.</returns>
    Task<string?> PickFileAsync(string title);
}
