using System.ComponentModel;

using Avalonia.Controls;
using Avalonia.Threading;

using BackupZCrypt.Desktop.ViewModels;

namespace BackupZCrypt.Desktop.Views;

/// <summary>
/// Modal dialog that presents the warnings confirmation flow and the final operation result, styled
/// like the rest of the application. It closes itself once neither state is active.
/// </summary>
public sealed partial class OperationDialog : Window
{
    private OperationViewModelBase? viewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="OperationDialog"/> class.
    /// </summary>
    public OperationDialog()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (DataContext is OperationViewModelBase vm)
        {
            viewModel = vm;
            vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        if (viewModel is not null)
        {
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            viewModel = null;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(CloseWhenIdle);
    }

    private void CloseWhenIdle()
    {
        if (viewModel is { IsRunning: false, ShowWarnings: false, HasResult: false })
        {
            Close();
        }
    }
}
