using System.Reflection;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;

using BackupZCrypt.Application.Backup;
using BackupZCrypt.Application.Common;
using BackupZCrypt.Desktop.Controls;
using BackupZCrypt.Desktop.Resources;
using BackupZCrypt.Desktop.ViewModels;
using BackupZCrypt.Desktop.ViewModels.Operations;
using BackupZCrypt.Desktop.Views;
using BackupZCrypt.Test.Common;

using Microsoft.Extensions.DependencyInjection;

namespace BackupZCrypt.Test.Unit.Desktop.Views;

/// <summary>
/// UI tests for the operation overlay: <see cref="OperationStatusView"/> opens an <see cref="OperationDialog"/>
/// over the window whenever a page's operation has something to show, the dialog shows the panel of each phase
/// and answers Escape and Enter, keeps input away from the page underneath, keeps the window open while the
/// operation runs, and goes away once the operation is idle again.
/// </summary>
public sealed class OperationDialogTests
{
    [Fact]
    internal Task Running_ShowsProgressAndEscapeCancelsIntoTheResultThatEnterDismisses()
    {
        return WithCreatePageAsync(async (window, page) =>
        {
            var running = page.Session.RunAsync(Run(static async (_, _, token) =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return Completed();
            }));

            var dialog = await WaitForDialogAsync(window);
            await HeadlessUi.WaitUntilAsync(() => PanelVisible(dialog, "RunningPanel"));

            window.KeyPress(Key.A, RawInputModifiers.None, PhysicalKey.A, null);
            Dispatcher.UIThread.RunJobs();
            var phaseAfterOtherKey = page.Session.Phase;

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await running;
            await HeadlessUi.WaitUntilAsync(() => PanelVisible(dialog, "ResultPanel"));
            var resultTitle = page.Session.ResultTitle;

            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await dialog.Completion;
            await HeadlessUi.WaitUntilAsync(() => FindDialog(window) is null);

            Assert.Multiple(
                () => Assert.Equal(OperationPhase.Running, phaseAfterOtherKey),
                () => Assert.Equal(Strings.ResultCancelled, resultTitle),
                () => Assert.Equal(OperationPhase.Idle, page.Session.Phase),
                () => Assert.False(PanelVisible(dialog, "RunningPanel"))
            );
        });
    }

    [Fact]
    internal Task Warnings_EnterIsIgnoredAndEscapeDismissesThem()
    {
        return WithCreatePageAsync(async (window, page) =>
        {
            await page.Session.RunAsync(Run(static (_, _, _) => Task.FromResult(Warned())));

            var dialog = await WaitForDialogAsync(window);
            await HeadlessUi.WaitUntilAsync(() => PanelVisible(dialog, "WarningsPanel"));

            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Dispatcher.UIThread.RunJobs();
            var phaseAfterEnter = page.Session.Phase;

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await dialog.Completion;

            Assert.Multiple(
                () => Assert.Equal(OperationPhase.AwaitingConfirmation, phaseAfterEnter),
                () => Assert.Equal(OperationPhase.Idle, page.Session.Phase)
            );
        });
    }

    [Fact]
    internal Task Warnings_ContinuingRunsTheOperationAgainInTheSameDialog()
    {
        return WithCreatePageAsync(async (window, page) =>
        {
            await page.Session.RunAsync(Run(static (proceed, _, _) =>
                Task.FromResult(proceed ? Completed() : Warned())));

            var dialog = await WaitForDialogAsync(window);
            await HeadlessUi.WaitUntilAsync(() => PanelVisible(dialog, "WarningsPanel"));

            await page.Session.ContinueAnywayCommand.ExecuteAsync(null);
            await HeadlessUi.WaitUntilAsync(() => PanelVisible(dialog, "ResultPanel"));

            Assert.Multiple(
                () => Assert.Same(dialog, FindDialog(window)),
                () => Assert.False(PanelVisible(dialog, "WarningsPanel")),
                () => Assert.True(page.Session.ResultIsSuccess)
            );

            page.Session.DismissResultCommand.Execute(null);
            await dialog.Completion;
        });
    }

    [Fact]
    internal Task Result_ListsTheErrorsAndWarningsOfAPartialRun()
    {
        return WithCreatePageAsync(async (window, page) =>
        {
            await page.Session.RunAsync(Run(static (_, _, _) => Task.FromResult(
                Result<BackupOutcome>.Success(
                    BackupOutcome.Completed(
                        new BackupResult(
                            TimeSpan.FromSeconds(3),
                            4096,
                            1,
                            2,
                            [new LocalizableMessage(MessageCode.UnexpectedErrorFormat, "disk")],
                            [new LocalizableMessage(MessageCode.DestinationContainsBackup)]
                        )
                    )
                ))));

            var dialog = await WaitForDialogAsync(window);
            await HeadlessUi.WaitUntilAsync(() => PanelVisible(dialog, "ResultPanel"));
            string?[] expected = [page.Session.Errors.Single(), page.Session.ResultWarnings.Single()];

            await HeadlessUi.WaitUntilAsync(() =>
            {
                var shown = dialog.GetLogicalDescendants().OfType<TextBlock>().Select(static t => t.Text).ToHashSet();
                return expected.All(shown.Contains);
            });

            page.Session.DismissResultCommand.Execute(null);
            await dialog.Completion;
        });
    }

    [Fact]
    internal Task OpenDialog_KeepsKeysAndTextAwayFromThePageAndGivesFocusBackWhenItCloses()
    {
        return WithCreatePageAsync(async (window, page) =>
        {
            var passwordBox = window.GetLogicalDescendants().OfType<PasswordTextBox>().First();
            passwordBox.Focus();
            var release = new TaskCompletionSource<Result<BackupOutcome>>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

            var running = page.Session.RunAsync(Run((_, _, _) => release.Task));
            var dialog = await WaitForDialogAsync(window);
            var card = dialog.FindControl<Border>("Card")!;
            await HeadlessUi.WaitUntilAsync(() => card.IsFocused);

            passwordBox.Focus();
            window.KeyPress(Key.A, RawInputModifiers.None, PhysicalKey.A, "a");
            window.KeyTextInput("a");
            Dispatcher.UIThread.RunJobs();
            var focusReturnedToCard = card.IsFocused;

            release.SetResult(Completed());
            await running;
            page.Session.DismissResultCommand.Execute(null);
            await dialog.Completion;
            await HeadlessUi.WaitUntilAsync(() => FindDialog(window) is null);

            Assert.Multiple(
                () => Assert.True(focusReturnedToCard),
                () => Assert.True(string.IsNullOrEmpty(passwordBox.Text)),
                () => Assert.True(passwordBox.IsFocused)
            );
        });
    }

    [Fact]
    internal Task WhileRunning_TheUserCannotCloseTheWindowButTheApplicationCan()
    {
        return WithCreatePageAsync(async (window, page) =>
        {
            var release = new TaskCompletionSource<Result<BackupOutcome>>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            var running = page.Session.RunAsync(Run((_, _, _) => release.Task));
            _ = await WaitForDialogAsync(window);
            var userCloseRefused = RequestClose(window, WindowCloseReason.WindowClosing, isProgrammatic: false);
            var programmaticRefused = RequestClose(window, WindowCloseReason.WindowClosing, isProgrammatic: true);
            var shutdownRefused = RequestClose(window, WindowCloseReason.OSShutdown, isProgrammatic: false);

            release.SetResult(Completed());
            await running;
            var userCloseRefusedWhenDone = RequestClose(window, WindowCloseReason.WindowClosing, isProgrammatic: false);

            Assert.Multiple(
                () => Assert.True(userCloseRefused),
                () => Assert.False(programmaticRefused),
                () => Assert.False(shutdownRefused),
                () => Assert.False(userCloseRefusedWhenDone)
            );
        });
    }

    [Fact]
    internal Task ClosingTheWindowMidRun_CompletesTheDialog()
    {
        return HeadlessUi.RunAsync(async () =>
        {
            using var services = DesktopShell.CreateServices();
            var page = services.GetRequiredService<CreateBackupViewModel>();
            var window = HeadlessUi.Show(new CreateBackupView { DataContext = page });
            var release = new TaskCompletionSource<Result<BackupOutcome>>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            var running = page.Session.RunAsync(Run((_, _, _) => release.Task));
            var dialog = await WaitForDialogAsync(window);

            window.Close();
            await dialog.Completion;
            release.SetResult(Completed());
            await running;

            Assert.Equal(OperationPhase.Completed, page.Session.Phase);
        });
    }

    [Fact]
    internal Task Dialog_OutsideAWindowOrWithoutAnOperation_StaysInertAndFillsTheOfferedSpace()
    {
        return HeadlessUi.RunAsync(() =>
        {
            OperationSession idle = new();
            OperationDialog detached = new() { DataContext = idle };
            OperationDialog empty = new() { DataContext = "not an operation" };

            detached.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var unbounded = detached.DesiredSize;
            detached.Measure(new Size(700, 500));
            var bounded = detached.DesiredSize;

            var window = HeadlessUi.Show(empty);
            empty.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            var userCloseRefused = RequestClose(window, WindowCloseReason.WindowClosing, isProgrammatic: false);
            window.Close();

            Assert.Multiple(
                () => Assert.False(detached.Completion.IsCompleted),
                () => Assert.True(unbounded.Width < 700),
                () => Assert.Equal(new Size(700, 500), bounded),
                () => Assert.True(empty.Completion.IsCompleted),
                () => Assert.False(userCloseRefused)
            );
        });
    }

    [Fact]
    internal Task StatusView_IgnoresPagesWithoutAnOperationAndStopsWatchingOnceUnloaded()
    {
        return HeadlessUi.RunAsync(async () =>
        {
            using var services = DesktopShell.CreateServices();
            var page = services.GetRequiredService<CreateBackupViewModel>();
            OperationStatusView status = new() { DataContext = "not a page" };
            var window = HeadlessUi.Show(status);

            status.DataContext = page;
            status.DataContext = page;
            window.Content = null;
            Dispatcher.UIThread.RunJobs();

            await page.Session.RunAsync(Run(static (_, _, _) => Task.FromResult(Completed())));
            Dispatcher.UIThread.RunJobs();

            Assert.Null(FindDialog(window));
            window.Close();
        });
    }

    /// <summary>
    /// Shows the create-backup page, whose status view presents its operation, runs the test, and closes the window.
    /// </summary>
    /// <param name="test">The test body.</param>
    /// <returns>A task that completes once the test has run on the UI thread.</returns>
    private static Task WithCreatePageAsync(Func<Window, CreateBackupViewModel, Task> test)
    {
        return HeadlessUi.RunAsync(async () =>
        {
            using var services = DesktopShell.CreateServices();
            var page = services.GetRequiredService<CreateBackupViewModel>();
            page.IsActivePage = true;
            var window = HeadlessUi.Show(new CreateBackupView { DataContext = page });

            try
            {
                await test(window, page);
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>
    /// Raises the closing notification a window sends before it closes, as the platform or the application would.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="reason">Why the window is closing.</param>
    /// <param name="isProgrammatic">Whether the application asked for the close.</param>
    /// <returns><see langword="true"/> if a handler cancelled the close.</returns>
    private static bool RequestClose(Window window, WindowCloseReason reason, bool isProgrammatic)
    {
        var args = (WindowClosingEventArgs)typeof(WindowClosingEventArgs)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static c => c.GetParameters().Length == 2)
            .Invoke([reason, isProgrammatic]);

        _ = typeof(Window)
            .GetMethod("OnClosing", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [args]);

        return args.Cancel;
    }

    /// <summary>
    /// Waits for the overlay to show a dialog.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <returns>The dialog.</returns>
    private static async Task<OperationDialog> WaitForDialogAsync(Window window)
    {
        await HeadlessUi.WaitUntilAsync(() => FindDialog(window) is { IsLoaded: true });
        return FindDialog(window)!;
    }

    /// <summary>
    /// Finds the dialog in the window's overlay layer.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <returns>The dialog, or <see langword="null"/> when none is shown.</returns>
    private static OperationDialog? FindDialog(Window window)
    {
        return OverlayLayer.GetOverlayLayer(window)?.Children.OfType<OperationDialog>().FirstOrDefault();
    }

    /// <summary>
    /// Returns whether a panel of the dialog is visible.
    /// </summary>
    /// <param name="dialog">The dialog.</param>
    /// <param name="name">The panel name.</param>
    /// <returns><see langword="true"/> if it is visible.</returns>
    private static bool PanelVisible(OperationDialog dialog, string name)
    {
        return dialog.FindControl<StackPanel>(name)!.IsVisible;
    }

    /// <summary>
    /// Wraps an execution delegate in a run with fixed titles.
    /// </summary>
    /// <param name="execute">The execution.</param>
    /// <returns>The run.</returns>
    private static OperationRun Run(OperationExecution execute)
    {
        return new OperationRun(execute, "success", "partial", "failure");
    }

    /// <summary>
    /// Builds a completed outcome over one successful file.
    /// </summary>
    /// <returns>The outcome.</returns>
    private static Result<BackupOutcome> Completed()
    {
        return Result<BackupOutcome>.Success(
            BackupOutcome.Completed(new BackupResult(TimeSpan.FromSeconds(1), 16, 1, 1))
        );
    }

    /// <summary>
    /// Builds an outcome that asks the user to confirm a warning.
    /// </summary>
    /// <returns>The outcome.</returns>
    private static Result<BackupOutcome> Warned()
    {
        return Result<BackupOutcome>.Success(
            BackupOutcome.AwaitingConfirmation([new LocalizableMessage(MessageCode.DestinationContainsBackup)])
        );
    }
}
