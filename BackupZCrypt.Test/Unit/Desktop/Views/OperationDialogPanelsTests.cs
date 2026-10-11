using BackupZCrypt.Desktop.ViewModels.Operations;
using BackupZCrypt.Desktop.Views;

namespace BackupZCrypt.Test.Unit.Desktop.Views;

/// <summary>
/// Unit tests for the operation dialog's panel selection and keyboard commands: which panel each session
/// phase shows, and that Escape always backs out while Enter only ever closes a result and never confirms
/// warnings.
/// </summary>
public sealed class OperationDialogPanelsTests
{
    /// <summary>
    /// Builds an idle operation session whose commands the tests compare against.
    /// </summary>
    /// <returns>The operation the dialog would present.</returns>
    private static OperationSession CreateOperation()
    {
        return new OperationSession();
    }

    [Theory]
    [InlineData(OperationPhase.Idle, OperationDialogPanel.None)]
    [InlineData(OperationPhase.Running, OperationDialogPanel.Running)]
    [InlineData(OperationPhase.AwaitingConfirmation, OperationDialogPanel.Warnings)]
    [InlineData(OperationPhase.Completed, OperationDialogPanel.Result)]
    internal void For_EachPhase_ShowsItsOwnPanel(OperationPhase phase, OperationDialogPanel expected)
    {
        Assert.Equal(expected, OperationDialogPanels.For(phase));
    }

    [Fact]
    internal void EscapeCommand_OnEachPanel_BacksOutOfWhatThePanelShows()
    {
        var operation = CreateOperation();

        Assert.Multiple(
            () =>
                Assert.Same(
                    operation.CancelOperationCommand,
                    OperationDialogPanels.EscapeCommand(OperationDialogPanel.Running, operation)
                ),
            () =>
                Assert.Same(
                    operation.DismissWarningsCommand,
                    OperationDialogPanels.EscapeCommand(OperationDialogPanel.Warnings, operation)
                ),
            () =>
                Assert.Same(
                    operation.DismissResultCommand,
                    OperationDialogPanels.EscapeCommand(OperationDialogPanel.Result, operation)
                ),
            () => Assert.Null(OperationDialogPanels.EscapeCommand(OperationDialogPanel.None, operation))
        );
    }

    [Theory]
    [InlineData(OperationDialogPanel.None)]
    [InlineData(OperationDialogPanel.Running)]
    [InlineData(OperationDialogPanel.Warnings)]
    internal void EnterCommand_OutsideTheResult_DoesNothingSoItNeverConfirmsWarnings(
        OperationDialogPanel panel
    )
    {
        Assert.Null(OperationDialogPanels.EnterCommand(panel, CreateOperation()));
    }

    [Fact]
    internal void EnterCommand_OnTheResult_ClosesIt()
    {
        var operation = CreateOperation();

        Assert.Same(
            operation.DismissResultCommand,
            OperationDialogPanels.EnterCommand(OperationDialogPanel.Result, operation)
        );
    }
}
