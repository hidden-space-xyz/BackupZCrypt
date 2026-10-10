using BackupZCrypt.Application.Backup;
using BackupZCrypt.Application.Settings;
using BackupZCrypt.Desktop.Services.Interfaces;
using BackupZCrypt.Desktop.ViewModels;
using BackupZCrypt.Desktop.Views;

using NSubstitute;

namespace BackupZCrypt.Test.Unit.Desktop;

/// <summary>
/// Unit tests for the operation dialog's panel selection and keyboard commands: which panel each operation
/// state shows, and that Escape always backs out while Enter only ever closes a result and never confirms
/// warnings.
/// </summary>
public sealed class OperationDialogPanelsTests
{
    /// <summary>
    /// Builds a real operation page over substituted services, whose state flags the tests set directly.
    /// </summary>
    /// <returns>The operation the dialog would present.</returns>
    private static RestoreBackupViewModel CreateOperation()
    {
        return new RestoreBackupViewModel(
            Substitute.For<IBackupOperations>(),
            Substitute.For<ISettingsStore>(),
            Substitute.For<IFilePickerService>()
        );
    }

    [Theory]
    [InlineData(false, false, false, OperationDialogPanel.None)]
    [InlineData(true, false, false, OperationDialogPanel.Running)]
    [InlineData(false, true, false, OperationDialogPanel.Warnings)]
    [InlineData(false, false, true, OperationDialogPanel.Result)]
    [InlineData(true, true, true, OperationDialogPanel.Running)]
    [InlineData(false, true, true, OperationDialogPanel.Warnings)]
    internal void For_OperationState_ShowsTheRunThenTheWarningsThenTheResult(
        bool isRunning,
        bool showWarnings,
        bool hasResult,
        OperationDialogPanel expected
    )
    {
        var operation = CreateOperation();
        operation.IsRunning = isRunning;
        operation.ShowWarnings = showWarnings;
        operation.HasResult = hasResult;

        Assert.Equal(expected, OperationDialogPanels.For(operation));
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
