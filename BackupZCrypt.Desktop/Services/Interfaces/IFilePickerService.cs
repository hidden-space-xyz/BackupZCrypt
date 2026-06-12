namespace BackupZCrypt.Desktop.Services.Interfaces;

public interface IFilePickerService
{
    Task<string?> PickFolderAsync(string title);

    Task<string?> PickFileAsync(string title);
}
