namespace BackupZCrypt.Desktop.ViewModels.Operations;

/// <summary>
/// Where an operation session stands, which decides the one panel the operation dialog shows.
/// </summary>
internal enum OperationPhase
{
    /// <summary>
    /// No operation is running and nothing is waiting for the user.
    /// </summary>
    Idle,

    /// <summary>
    /// An operation is running.
    /// </summary>
    Running,

    /// <summary>
    /// The operation stopped before running to ask the user to confirm its warnings.
    /// </summary>
    AwaitingConfirmation,

    /// <summary>
    /// The operation finished, failed, or was cancelled, and its result is shown.
    /// </summary>
    Completed,
}
