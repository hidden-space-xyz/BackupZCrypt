namespace BackupZCrypt.Desktop.Services.Interfaces;

/// <summary>
/// Writes text to the system clipboard.
/// </summary>
public interface IClipboardService
{
    /// <summary>
    /// Copies the supplied text to the system clipboard.
    /// </summary>
    /// <param name="text">The text to place on the clipboard.</param>
    /// <returns>A task that completes once the clipboard has been updated.</returns>
    Task SetTextAsync(string text);
}
