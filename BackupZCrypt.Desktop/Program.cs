using Avalonia;

namespace BackupZCrypt.Desktop;

/// <summary>
/// Application entry point that builds and starts the Avalonia desktop host.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Starts the application with the classic desktop lifetime; this is the process entry point.
    /// </summary>
    /// <param name="args">The command-line arguments forwarded to the Avalonia lifetime.</param>
    [STAThread]
    public static void Main(string[] args)
    {
        _ = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Configures the Avalonia application builder. Also referenced by the visual designer, so it must not be removed.
    /// </summary>
    /// <returns>The configured <see cref="AppBuilder"/>.</returns>
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
    }
}
