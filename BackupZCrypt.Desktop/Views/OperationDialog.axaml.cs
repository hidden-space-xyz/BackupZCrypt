using System.ComponentModel;
using System.Windows.Input;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using BackupZCrypt.Desktop.ViewModels;

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
/// application's control.
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
    private OperationViewModelBase? viewModel;

    /// <summary>
    /// The top level hosting the overlay while it is attached, used to intercept input aimed at the page
    /// underneath.
    /// </summary>
    private TopLevel? topLevel;

    /// <summary>
    /// The window hosting the overlay while it is attached, used to move it by the scrim and to guard its
    /// closing.
    /// </summary>
    private Window? window;

    /// <summary>
    /// The element that had keyboard focus before the overlay opened, restored when the overlay goes away.
    /// </summary>
    private IInputElement? previousFocus;

    /// <summary>
    /// The panel currently on display. It deliberately survives the operation going idle, so the card keeps its
    /// content while it animates out instead of collapsing to an empty frame.
    /// </summary>
    private DialogState shownState;

    /// <summary>
    /// A value indicating whether a state refresh is already queued on the dispatcher.
    /// </summary>
    private bool syncQueued;

    /// <summary>
    /// A value indicating whether the exit motion has started, after which the displayed state is frozen.
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
    /// The panels the card can show, one per phase of an operation.
    /// </summary>
    private enum DialogState
    {
        /// <summary>
        /// Nothing to show: the operation is idle.
        /// </summary>
        None,

        /// <summary>
        /// The operation is running and reports its progress.
        /// </summary>
        Running,

        /// <summary>
        /// The operation stopped at warnings the user has to confirm or dismiss.
        /// </summary>
        Warnings,

        /// <summary>
        /// The operation finished and its outcome is shown.
        /// </summary>
        Result,
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

        viewModel = DataContext as OperationViewModelBase;
        Sync();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Starts observing the operation and intercepting input, remembers the focused element, and guards the
    /// window against being closed while the operation runs.
    /// </remarks>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        viewModel?.PropertyChanged += OnViewModelPropertyChanged;

        topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is not null)
        {
            previousFocus = topLevel.FocusManager?.GetFocusedElement();
            topLevel.AddHandler(KeyDownEvent, OnTopLevelKeyDown, RoutingStrategies.Tunnel);
            topLevel.AddHandler(TextInputEvent, OnTopLevelTextInput, RoutingStrategies.Tunnel);
        }

        window = topLevel as Window;
        window?.Closing += OnWindowClosing;

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

        topLevel?.RemoveHandler(KeyDownEvent, OnTopLevelKeyDown);
        topLevel?.RemoveHandler(TextInputEvent, OnTopLevelTextInput);
        topLevel = null;

        window?.Closing -= OnWindowClosing;
        window = null;

        if (
            previousFocus is Visual previous
            && TopLevel.GetTopLevel(previous) is not null
            && previousFocus.Focusable
        )
        {
            _ = previousFocus.Focus();
        }

        previousFocus = null;
        _ = completion.TrySetResult();
    }

    /// <summary>
    /// Handles Escape and Enter for the panel on display: Escape backs out (cancel, dismiss, or close) and
    /// Enter confirms (continue or close). A focused button handles Enter itself before it bubbles up here.
    /// </summary>
    /// <param name="e">The key event.</param>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        base.OnKeyDown(e);

        if (e.Handled || viewModel is null || closing)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                Execute(
                    shownState switch
                    {
                        DialogState.Running => viewModel.CancelOperationCommand,
                        DialogState.Warnings => viewModel.DismissWarningsCommand,
                        DialogState.Result => viewModel.DismissResultCommand,
                        DialogState.None => null,
                        _ => null,
                    }
                );
                e.Handled = true;
                break;

            case Key.Enter:
                Execute(
                    shownState switch
                    {
                        DialogState.Warnings => viewModel.ContinueAnywayCommand,
                        DialogState.Result => viewModel.DismissResultCommand,
                        DialogState.Running => null,
                        DialogState.None => null,
                        _ => null,
                    }
                );
                e.Handled = true;
                break;

            default:
                break;
        }
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
    /// Queues a single state refresh when the running, warnings, or result state of the operation changes.
    /// </summary>
    /// <remarks>
    /// The refresh is deferred so it sees the operation after a whole batch of changes: starting a run first
    /// clears the result and only then raises the running flag, and reacting in between would close the overlay
    /// for an operation that is just beginning.
    /// </remarks>
    /// <param name="sender">The operation that raised the event.</param>
    /// <param name="e">The property change notification.</param>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (
            syncQueued
            || e.PropertyName
                is not (
                    nameof(OperationViewModelBase.IsRunning)
                    or nameof(OperationViewModelBase.ShowWarnings)
                    or nameof(OperationViewModelBase.HasResult)
                )
        )
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

        var state = viewModel switch
        {
            { IsRunning: true } => DialogState.Running,
            { ShowWarnings: true } => DialogState.Warnings,
            { HasResult: true } => DialogState.Result,
            _ => DialogState.None,
        };

        if (state is DialogState.None)
        {
            if (topLevel is not null)
            {
                _ = CloseAsync();
            }

            return;
        }

        if (state == shownState)
        {
            return;
        }

        shownState = state;

        SetPanelVisible("RunningPanel", state is DialogState.Running);
        SetPanelVisible("WarningsPanel", state is DialogState.Warnings);
        SetPanelVisible("ResultPanel", state is DialogState.Result);

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

    /// <summary>
    /// Returns whether an input event originated from the overlay itself or one of its descendants.
    /// </summary>
    /// <param name="source">The event source.</param>
    /// <returns><see langword="true"/> if the source belongs to the overlay; otherwise <see langword="false"/>.</returns>
    private bool IsFromOverlay(object? source)
    {
        return source is Visual visual
            && (ReferenceEquals(visual, this) || this.IsVisualAncestorOf(visual));
    }

    /// <summary>
    /// Swallows key presses aimed at anything outside the overlay and pulls focus back into the card, so
    /// neither shortcuts nor default buttons of the page underneath can react while the overlay is open.
    /// </summary>
    /// <param name="sender">The top level.</param>
    /// <param name="e">The key event, seen during its tunnelling phase.</param>
    private void OnTopLevelKeyDown(object? sender, KeyEventArgs e)
    {
        if (IsFromOverlay(e.Source))
        {
            return;
        }

        e.Handled = true;
        FocusCard();
    }

    /// <summary>
    /// Swallows text typed into anything outside the overlay, such as a text box of the page underneath that
    /// still held focus.
    /// </summary>
    /// <param name="sender">The top level.</param>
    /// <param name="e">The text input event, seen during its tunnelling phase.</param>
    private void OnTopLevelTextInput(object? sender, TextInputEventArgs e)
    {
        if (!IsFromOverlay(e.Source))
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// Keeps the window open while the operation runs, which a modal dialog window used to guarantee by
    /// disabling its owner. Closing the application mid-run would abandon the operation halfway; the user can
    /// cancel it from the overlay first. Programmatic closes and system shutdown are never blocked.
    /// </summary>
    /// <param name="sender">The window being closed.</param>
    /// <param name="e">The closing event, which can be cancelled.</param>
    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (
            viewModel is { IsRunning: true }
            && e.CloseReason is WindowCloseReason.WindowClosing
            && !e.IsProgrammatic
        )
        {
            e.Cancel = true;
        }
    }

    /// <summary>
    /// Lets the user move the window by dragging the dimmed area around the card, since the overlay covers the
    /// title bar.
    /// </summary>
    /// <param name="sender">The scrim that raised the event.</param>
    /// <param name="e">The pointer event carrying the button state.</param>
    private void OnScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        if (window is not null && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            window.BeginMoveDrag(e);
        }
    }
}
