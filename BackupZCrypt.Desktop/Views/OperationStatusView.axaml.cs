using System.ComponentModel;

using Avalonia.Controls;
using Avalonia.Interactivity;

using BackupZCrypt.Desktop.ViewModels;

namespace BackupZCrypt.Desktop.Views;

/// <summary>
/// Reusable control that displays live operation progress inline and surfaces the warnings
/// confirmation and final result in a modal dialog.
/// </summary>
public sealed partial class OperationStatusView : UserControl
{
    private OperationViewModelBase? viewModel;
    private bool dialogOpen;

    /// <summary>
    /// Initializes a new instance of the <see cref="OperationStatusView"/> class.
    /// </summary>
    public OperationStatusView()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        Hook(DataContext as OperationViewModelBase);
    }

    /// <inheritdoc />
    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        Hook(null);
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Hook(DataContext as OperationViewModelBase);
    }

    private void Hook(OperationViewModelBase? vm)
    {
        if (ReferenceEquals(viewModel, vm))
        {
            return;
        }

        if (viewModel is not null)
        {
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        viewModel = vm;

        if (viewModel is not null)
        {
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private async void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (
            e.PropertyName
            is not (
                nameof(OperationViewModelBase.IsRunning)
                or nameof(OperationViewModelBase.ShowWarnings)
                or nameof(OperationViewModelBase.HasResult)
            )
        )
        {
            return;
        }

        await TryShowDialogAsync();
    }

    private async Task TryShowDialogAsync()
    {
        if (dialogOpen || viewModel is null)
        {
            return;
        }

        if (!viewModel.IsRunning && !viewModel.ShowWarnings && !viewModel.HasResult)
        {
            return;
        }

        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        dialogOpen = true;
        try
        {
            var dialog = new OperationDialog { DataContext = viewModel };
            await dialog.ShowDialog(owner);
        }
        finally
        {
            dialogOpen = false;
        }
    }
}
