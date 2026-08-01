using System.ComponentModel;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

using BackupZCrypt.Desktop.ViewModels;

namespace BackupZCrypt.Desktop.Views;

/// <summary>
/// Modal dialog that presents the warnings confirmation flow and the final operation result, styled
/// like the rest of the application. It closes itself once neither state is active.
/// </summary>
internal sealed partial class OperationDialog : Window
{
    /// <summary>
    /// The view model whose state changes are being observed, or <see langword="null"/> while the
    /// dialog is not subscribed.
    /// </summary>
    private OperationViewModelBase? viewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="OperationDialog"/> class.
    /// </summary>
    public OperationDialog()
    {
        InitializeComponent();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Clears the card's drop shadow and the margin reserved for it when the window was not granted
    /// per-pixel transparency (for example X11 without a compositor): the window background then
    /// paints opaque and the margin would show as a solid band around the card. Also starts observing
    /// the view model, so the dialog can close itself once the operation goes idle.
    /// </remarks>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (
            ActualTransparencyLevel != WindowTransparencyLevel.Transparent
            && this.FindControl<Border>("DialogCard") is { } dialogCard
        )
        {
            dialogCard.Margin = new Thickness(0);
            dialogCard.BoxShadow = default;
        }

        if (DataContext is OperationViewModelBase vm)
        {
            viewModel = vm;
            vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    /// <inheritdoc/>
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        viewModel?.PropertyChanged -= OnViewModelPropertyChanged;
        viewModel = null;
    }

    /// <summary>
    /// Starts a window move drag when the dialog card is pressed with the left button.
    /// </summary>
    /// <remarks>
    /// The window is undecorated, so the card itself has to provide the move affordance a system
    /// title bar would normally give. Buttons and other interactive content mark the event handled
    /// before it bubbles up here, so this never steals a click from them.
    /// </remarks>
    /// <param name="sender">The card that raised the event.</param>
    /// <param name="e">The pointer event carrying the button state.</param>
    private void OnCardPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    /// <summary>
    /// Queues a close check on the UI thread whenever the view model reports a state change.
    /// </summary>
    /// <param name="sender">The view model that raised the event.</param>
    /// <param name="e">The property change notification.</param>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(CloseWhenIdle);
    }

    /// <summary>
    /// Closes the dialog once the operation is no longer running and neither the warnings nor the
    /// result panel has anything left to show.
    /// </summary>
    private void CloseWhenIdle()
    {
        if (viewModel is { IsRunning: false, ShowWarnings: false, HasResult: false })
        {
            Close();
        }
    }
}
