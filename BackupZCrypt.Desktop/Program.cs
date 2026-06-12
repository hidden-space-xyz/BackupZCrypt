using Avalonia;

namespace BackupZCrypt.Desktop;

internal static class Program
{
    // Avalonia configuration: do not remove, also used by the visual designer.
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
    }
}
