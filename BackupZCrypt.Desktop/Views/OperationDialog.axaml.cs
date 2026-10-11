using System.ComponentModel;
using System.Windows.Input;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

using BackupZCrypt.Desktop.Behaviors;
using BackupZCrypt.Desktop.ViewModels.Operations;

namespace BackupZCrypt.Desktop.Views;

/// <summary>
/// In-window modal overlay that presents the progress, the warnings confirmation, and the final result of an
/// operation over the dimmed application. It covers the whole window, keeps pointer and keyboard input away
/// from the page underneath, and completes <see cref="Completion"/> once it has animated out.
/// </summary>
/// <remarks>
/// The overlay replaces a separate top-level dialog window. A second native window could not dim the
/// application behind it, its size snapped whenever the content switched from progress to result, and its
/// appearance competed with the platform's own window animations. Drawn in the same surface as the rest of
/// the application, the card stays centered while its content changes, and every bit of motion is under the
/// application's control. Keeping input inside the overlay, guarding the window against closing mid-run, and
/// moving the window by the scrim are the jobs of <see cref="ModalInputScope"/>,
/// <see cref="WindowCloseGuard"/>, and the scrim's <see cref="Controls.WindowDragArea"/>; the panel each
/// state shows and the keys' commands come from <see cref="OperationDialogPanels"/>.
/// </remarks>
internal sealed partial class OperationDialog : UserControl
{
    /// <summary>
    /// How long the exit motion is given before the overlay reports completion. It matches the longest fade of
    /// the scrim and the card, so the overlay is never removed while it is still visible.
    /// </summary>
    private static readonly TimeSpan ExitDuration = TimeSpan.FromMilliseconds(260);

    /// <summary>
    /// The source behind <see cref="Completion"/>, completed once the overlay has finished closing or has been
    /// removed from the visual tree.
    /// </summary>
    private readonly TaskCompletionSource completion = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

    /// <summary>
    /// The operation being presented, or <see langword="null"/> before a data context is assigned.
    /// </summary>
    private OperationSession? viewModel;

    /// <summary>
    /// The scope keeping keyboard input inside the overlay while it is attached.
    /// </summary>
    private ModalInputScope? inputScope;

    /// <summary>
    /// The guard keeping the hosting window open while the operation runs, while the overlay is attached.
    /// </summary>
    private WindowCloseGuard? closeGuard;

    /// <summary>
    /// The panel currently on display. It deliberately survives the operation going idle, so the card keeps its
    /// content while it animates out instead of collapsing to an empty frame.
    /// </summary>
    private OperationDialogPanel shownPanel;

    /// <summary>
    /// A value indicating whether a state refresh is already queued on the dispatcher.
    /// </summary>
    private bool syncQueued;

    /// <summary>
    /// A value indicating whether the exit motion has started, after which the displayed panel is frozen.
    /// </summary>
    private bool closing;

    /// <summary>
    /// Initializes a new instance of the <see cref="OperationDialog"/> class.
    /// </summary>
    public OperationDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Gets a task that completes once the overlay has animated out or has been removed from the visual tree.
    /// </summary>
    public Task Completion => completion.Task;

    /// <summary>
    /// Claims the whole size offered by the overlay layer, which lays its children out like a canvas and would
    /// otherwise shrink the overlay to the size of its card.
    /// </summary>
    /// <param name="availableSize">The size offered by the overlay layer.</param>
    /// <returns>The offered size, or the content size along an unbounded dimension.</returns>
    protected override Size MeasureOverride(Size availableSize)
    {
        var desired = base.MeasureOverride(availableSize);

        return new Size(
            double.IsInfinity(availableSize.Width) ? desired.Width : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? desired.Height : availableSize.Height
        );
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Shows the panel for the operation's current state straight away, before the overlay is attached, so the
    /// first panel appears with the card instead of animating in on top of it.
    /// </remarks>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        viewModel = DataContext as OperationSession;
        Sync();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Starts observing the operation, keeps keyboard input inside the overlay, and guards the window against
    /// being closed while the operation runs.
    /// </remarks>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        viewModel?.PropertyChanged += OnViewModelPropertyChanged;

        inputScope = ModalInputScope.Open(this, FocusCard);
        closeGuard = WindowCloseGuard.Open(this, () => viewModel is { IsRunning: true });

        Sync();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Moves keyboard focus into the card and queues the entrance motion behind everything else pending.
    /// The overlay's first frame builds and lays out the whole card, and an animation started before it is
    /// painted would take the start of that frame as its time zero and appear to skip ahead. Queued at
    /// background priority, the entrance starts once the overlay has been painted, fully transparent, and plays
    /// from its first step.
    /// </remarks>
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        FocusCard();
        Dispatcher.UIThread.Post(() => Classes.Add("open"), DispatcherPriority.Background);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Releases every subscription, hands keyboard focus back to the element that had it, and completes
    /// <see cref="Completion"/> in case the overlay was removed without closing itself.
    /// </remarks>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        viewModel?.PropertyChanged -= OnViewModelPropertyChanged;

        inputScope?.Dispose();
        inputScope = null;

        closeGuard?.Dispose();
        closeGuard = null;

        _ = completion.TrySetResult();
    }

    /// <summary>
    /// Runs the command <see cref="OperationDialogPanels"/> assigns to Escape or Enter on the panel on display.
    /// A focused button handles Enter itself before it bubbles up here.
    /// </summary>
    /// <param name="e">The key event.</param>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        base.OnKeyDown(e);

        if (e.Handled || viewModel is null || closing || e.Key is not (Key.Escape or Key.Enter))
        {
            return;
        }

        Execute(
            e.Key is Key.Escape
                ? OperationDialogPanels.EscapeCommand(shownPanel, viewModel)
                : OperationDialogPanels.EnterCommand(shownPanel, viewModel)
        );
        e.Handled = true;
    }

    /// <summary>
    /// Runs a command of the operation when it can currently execute.
    /// </summary>
    /// <param name="command">The command to run, or <see langword="null"/> to do nothing.</param>
    private static void Execute(ICommand? command)
    {
        if (command?.CanExecute(null) == true)
        {
            command.Execute(null);
        }
    }

    /// <summary>
    /// Queues a single state refresh when the phase of the session changes.
    /// </summary>
    /// <remarks>
    /// The refresh is deferred so it runs on the UI thread after the change has settled, and several quick
    /// changes collapse into one refresh.
    /// </remarks>
    /// <param name="sender">The operation that raised the event.</param>
    /// <param name="e">The property change notification.</param>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (syncQueued || e.PropertyName is not nameof(OperationSession.Phase))
        {
            return;
        }

        syncQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            syncQueued = false;
            Sync();
        });
    }

    /// <summary>
    /// Shows the panel that matches the operation's state, or starts closing once the operation is idle.
    /// </summary>
    private void Sync()
    {
        if (viewModel is null || closing)
        {
            return;
        }

        var panel = OperationDialogPanels.For(viewModel.Phase);

        if (panel is OperationDialogPanel.None)
        {
            if (TopLevel.GetTopLevel(this) is not null)
            {
                _ = CloseAsync();
            }

            return;
        }

        if (panel == shownPanel)
        {
            return;
        }

        shownPanel = panel;

        SetPanelVisible("RunningPanel", panel is OperationDialogPanel.Running);
        SetPanelVisible("WarningsPanel", panel is OperationDialogPanel.Warnings);
        SetPanelVisible("ResultPanel", panel is OperationDialogPanel.Result);

        if (IsLoaded)
        {
            FocusCard();
        }
    }

    /// <summary>
    /// Shows or hides one of the state panels. The panels carry the shared reveal style, so the one being shown
    /// fades and rises into place.
    /// </summary>
    /// <param name="name">The name of the panel.</param>
    /// <param name="visible">Whether the panel should be shown.</param>
    private void SetPanelVisible(string name, bool visible)
    {
        if (this.FindControl<StackPanel>(name) is { } panel)
        {
            panel.IsVisible = visible;
        }
    }

    /// <summary>
    /// Plays the exit motion and then reports completion, so the presenter can remove the overlay.
    /// </summary>
    /// <returns>A task that completes once the overlay has faded out.</returns>
    private async Task CloseAsync()
    {
        if (closing)
        {
            return;
        }

        closing = true;
        Classes.Add("closing");

        await Task.Delay(ExitDuration);

        _ = completion.TrySetResult();
    }

    /// <summary>
    /// Moves keyboard focus to the card itself rather than to one of its buttons, so a stray Enter cannot
    /// trigger an action the user did not look at, while Escape and Enter still reach the overlay.
    /// </summary>
    private void FocusCard()
    {
        _ = this.FindControl<Border>("Card")?.Focus();
    }
}
