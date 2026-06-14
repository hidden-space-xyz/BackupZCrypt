namespace BackupZCrypt.Desktop.Services.Interfaces;

/// <summary>
/// Presents a native folder picker and returns the selected local path.
/// </summary>
public interface IFilePickerService
{
    /// <summary>
    /// Prompts the user to choose a single folder.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <returns>The selected folder's local path, or <see langword="null"/> if the dialog was cancelled.</returns>
    public Task<string?> PickFolderAsync(string title);
}
