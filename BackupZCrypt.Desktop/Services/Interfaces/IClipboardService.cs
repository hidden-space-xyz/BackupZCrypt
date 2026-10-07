namespace BackupZCrypt.Desktop.Services.Interfaces;

/// <summary>
/// Writes text to the system clipboard.
/// </summary>
internal interface IClipboardService
{
    /// <summary>
    /// Copies the supplied text to the system clipboard.
    /// </summary>
    /// <param name="text">The text to place on the clipboard.</param>
    /// <returns>A task that completes once the clipboard has been updated.</returns>
    public Task SetTextAsync(string text);

    /// <summary>
    /// Copies a secret to the system clipboard and clears it again after a short while, unless
    /// something else has been copied in the meantime.
    /// </summary>
    /// <param name="text">The secret to place on the clipboard.</param>
    /// <returns>A task that completes once the clipboard has been updated.</returns>
    public Task SetSensitiveTextAsync(string text);
}
