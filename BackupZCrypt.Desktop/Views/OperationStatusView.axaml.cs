using System.ComponentModel;

using Avalonia.Controls;
using Avalonia.Interactivity;

using BackupZCrypt.Desktop.ViewModels;

namespace BackupZCrypt.Desktop.Views;

/// <summary>
/// Reusable control that displays live operation progress inline and surfaces the warnings
/// confirmation and final result in a modal dialog.
/// </summary>
internal sealed partial class OperationStatusView : UserControl
{
    /// <summary>
    /// The view model whose state changes are being observed, or <see langword="null"/> while the
    /// control is not subscribed.
    /// </summary>
    private OperationViewModelBase? viewModel;

    /// <summary>
    /// A value indicating whether a dialog is already on screen, so a further state change cannot
    /// open a second one.
    /// </summary>
    private bool dialogOpen;

    /// <summary>
    /// Initializes a new instance of the <see cref="OperationStatusView"/> class.
    /// </summary>
    public OperationStatusView()
    {
        InitializeComponent();
    }

    /// <inheritdoc/>
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        Hook(DataContext as OperationViewModelBase);
    }

    /// <inheritdoc/>
    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        Hook(null);
    }

    /// <inheritdoc/>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Hook(DataContext as OperationViewModelBase);
    }

    /// <summary>
    /// Moves the property-changed subscription to the given view model, detaching from the previous
    /// one first so the control never leaves a handler behind.
    /// </summary>
    /// <param name="vm">The view model to observe, or <see langword="null"/> to only detach.</param>
    private void Hook(OperationViewModelBase? vm)
    {
        if (ReferenceEquals(viewModel, vm))
        {
            return;
        }

        viewModel?.PropertyChanged -= OnViewModelPropertyChanged;

        viewModel = vm;

        viewModel?.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>
    /// Opens the modal dialog when the running, warnings, or result state of the view model changes.
    /// </summary>
    /// <remarks>
    /// An event handler cannot return a task, so a fault escaping <see cref="TryShowDialogAsync"/>
    /// would be rethrown on the dispatcher and terminate the process — potentially mid-backup, and
    /// nothing else in the application observes it. Swallowing it costs only the dialog, which is
    /// the same posture every other handler in this assembly takes.
    /// </remarks>
    /// <param name="sender">The view model that raised the event.</param>
    /// <param name="e">The property change notification.</param>
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

        _ = await TryShowDialogAsync();
    }

    /// <summary>
    /// Shows the operation dialog over the owning window, unless one is already open, the view model
    /// has nothing to report, or the control is not yet attached to a window.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if the dialog was shown and dismissed; <see langword="false"/> if there was
    /// nothing to show or the dialog failed to open.
    /// </returns>
    private async Task<bool> TryShowDialogAsync()
    {
        if (dialogOpen || viewModel is null)
        {
            return false;
        }

        if (!viewModel.IsRunning && !viewModel.ShowWarnings && !viewModel.HasResult)
        {
            return false;
        }

        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return false;
        }

        dialogOpen = true;
        try
        {
            var dialog = new OperationDialog { DataContext = viewModel };
            await dialog.ShowDialog(owner);

            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }
        finally
        {
            dialogOpen = false;
        }
    }
}
