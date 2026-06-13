using Avalonia.Controls;

namespace BackupZCrypt.Desktop.Views;

/// <summary>
/// Reusable control that displays operation progress, the warnings/errors panels and the final result.
/// </summary>
public sealed partial class OperationStatusView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OperationStatusView"/> class.
    /// </summary>
    public OperationStatusView()
    {
        InitializeComponent();
    }
}
