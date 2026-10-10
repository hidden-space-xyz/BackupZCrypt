using System.Windows.Input;

using BackupZCrypt.Desktop.ViewModels;

namespace BackupZCrypt.Desktop.Views;

/// <summary>
/// Decides which panel the operation dialog shows for an operation's state, and which of the operation's
/// commands the Escape and Enter keys run on each panel.
/// </summary>
internal static class OperationDialogPanels
{
    /// <summary>
    /// Selects the panel that matches the operation's state. A running operation outranks pending warnings,
    /// and pending warnings outrank a finished result.
    /// </summary>
    /// <param name="operation">The operation being presented.</param>
    /// <returns>The panel to show, or <see cref="OperationDialogPanel.None"/> when the operation is idle.</returns>
    public static OperationDialogPanel For(OperationViewModelBase operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return operation switch
        {
            { IsRunning: true } => OperationDialogPanel.Running,
            { ShowWarnings: true } => OperationDialogPanel.Warnings,
            { HasResult: true } => OperationDialogPanel.Result,
            _ => OperationDialogPanel.None,
        };
    }

    /// <summary>
    /// Gets the command Escape runs on a panel: it backs out by cancelling the run, dismissing the warnings,
    /// or closing the result.
    /// </summary>
    /// <param name="panel">The panel on display.</param>
    /// <param name="operation">The operation being presented.</param>
    /// <returns>The command to run, or <see langword="null"/> when Escape does nothing on the panel.</returns>
    public static ICommand? EscapeCommand(OperationDialogPanel panel, OperationViewModelBase operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return panel switch
        {
            OperationDialogPanel.Running => operation.CancelOperationCommand,
            OperationDialogPanel.Warnings => operation.DismissWarningsCommand,
            OperationDialogPanel.Result => operation.DismissResultCommand,
            OperationDialogPanel.None => null,
            _ => null,
        };
    }

    /// <summary>
    /// Gets the command Enter runs on a panel: it closes the result and does nothing else.
    /// </summary>
    /// <remarks>
    /// Enter never confirms warnings: they announce consequences such as files leaving a backup or a backup
    /// being replaced, so continuing takes a deliberate press of the button rather than the same key that
    /// started the operation.
    /// </remarks>
    /// <param name="panel">The panel on display.</param>
    /// <param name="operation">The operation being presented.</param>
    /// <returns>The command to run, or <see langword="null"/> when Enter does nothing on the panel.</returns>
    public static ICommand? EnterCommand(OperationDialogPanel panel, OperationViewModelBase operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return panel switch
        {
            OperationDialogPanel.Result => operation.DismissResultCommand,
            OperationDialogPanel.Warnings => null,
            OperationDialogPanel.Running => null,
            OperationDialogPanel.None => null,
            _ => null,
        };
    }
}
