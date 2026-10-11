namespace BackupZCrypt.Desktop.ViewModels.Operations;

/// <summary>
/// One operation a page asks its session to run: the captured execution and how its result is titled.
/// </summary>
/// <param name="Execute">Runs the operation with the request the page captured when the user started it.</param>
/// <param name="SuccessTitle">The result title shown when every item succeeded.</param>
/// <param name="PartialTitle">The result title shown when the operation finished but some items failed.</param>
/// <param name="FailureTitle">The result title shown when the operation could not be performed.</param>
internal sealed record class OperationRun(
    OperationExecution Execute,
    string SuccessTitle,
    string PartialTitle,
    string FailureTitle
);
