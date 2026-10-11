namespace BackupZCrypt.Desktop.ViewModels;

/// <summary>
/// Names the icon of a navigation entry, leaving its drawing to the view so the ViewModels stay free
/// of UI framework types.
/// </summary>
internal enum NavigationIcon
{
    /// <summary>
    /// The create-backup page.
    /// </summary>
    CreateBackup,

    /// <summary>
    /// The update-backup page.
    /// </summary>
    UpdateBackup,

    /// <summary>
    /// The restore-backup page.
    /// </summary>
    RestoreBackup,

    /// <summary>
    /// The verify-backup page.
    /// </summary>
    VerifyBackup,

    /// <summary>
    /// The settings page.
    /// </summary>
    Settings,

    /// <summary>
    /// The about page.
    /// </summary>
    About,
}
