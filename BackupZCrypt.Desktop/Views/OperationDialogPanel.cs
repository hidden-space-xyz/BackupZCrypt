namespace BackupZCrypt.Desktop.Views;

/// <summary>
/// The panels the operation dialog can show, one per phase of an operation.
/// </summary>
internal enum OperationDialogPanel
{
    /// <summary>
    /// Nothing to show: the operation is idle.
    /// </summary>
    None,

    /// <summary>
    /// The operation is running and reports its progress.
    /// </summary>
    Running,

    /// <summary>
    /// The operation stopped at warnings the user has to confirm or dismiss.
    /// </summary>
    Warnings,

    /// <summary>
    /// The operation finished and its outcome is shown.
    /// </summary>
    Result,
}
