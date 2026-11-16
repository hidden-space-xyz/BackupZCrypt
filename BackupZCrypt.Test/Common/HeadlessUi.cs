using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;

using BackupZCrypt.Desktop;
using BackupZCrypt.Test.Common;

[assembly: AvaloniaTestApplication(typeof(HeadlessUi))]

namespace BackupZCrypt.Test.Common;

/// <summary>
/// Runs Desktop tests on the UI thread of a single headless Avalonia session that hosts the real
/// <see cref="App"/>, so views load their compiled XAML, styles, and resources exactly as in the application.
/// </summary>
public static class HeadlessUi
{
    /// <summary>
    /// The longest a test waits for the UI to reach an expected state before failing.
    /// </summary>
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The session shared by every UI test of the assembly, started on first use.
    /// </summary>
    private static readonly Lazy<HeadlessUnitTestSession> Session = new(
        static () => HeadlessUnitTestSession.GetOrStartForAssembly(typeof(HeadlessUi).Assembly)
    );

    /// <summary>
    /// Builds the application the headless session runs: the real application on the headless platform.
    /// </summary>
    /// <returns>The configured builder.</returns>
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder
            .Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true })
            .WithInterFont();
    }

    /// <summary>
    /// Runs an asynchronous test body on the UI thread and waits for it to finish.
    /// </summary>
    /// <param name="body">The test body.</param>
    /// <returns>A task that completes once the body has finished.</returns>
    public static Task RunAsync(Func<Task> body)
    {
        ArgumentNullException.ThrowIfNull(body);

        return Session.Value.Dispatch(
            async () =>
            {
                await body();
                return true;
            },
            TestContext.Current.CancellationToken
        );
    }

    /// <summary>
    /// Runs a synchronous test body on the UI thread and waits for it to finish.
    /// </summary>
    /// <param name="body">The test body.</param>
    /// <returns>A task that completes once the body has finished.</returns>
    public static Task RunAsync(Action body)
    {
        ArgumentNullException.ThrowIfNull(body);

        return Session.Value.Dispatch(body, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Shows a window hosting the given content and lets it complete its first layout pass.
    /// </summary>
    /// <param name="content">The content to host.</param>
    /// <returns>The shown window, which the caller closes.</returns>
    public static Window Show(object content)
    {
        Window window = new() { Width = 1000, Height = 800, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    /// <summary>
    /// Keeps the UI thread running until a condition holds.
    /// </summary>
    /// <param name="condition">The condition awaited.</param>
    /// <returns>A task that completes once the condition holds.</returns>
    /// <exception cref="TimeoutException">The condition did not hold in time.</exception>
    public static async Task WaitUntilAsync(Func<bool> condition)
    {
        ArgumentNullException.ThrowIfNull(condition);

        var deadline = DateTime.UtcNow + WaitTimeout;

        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The UI did not reach the expected state in time.");
            }

            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
    }
}
