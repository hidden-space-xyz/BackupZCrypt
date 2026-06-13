using System.Globalization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects.Backup;
using BackupZCrypt.Composition;
using BackupZCrypt.Desktop.Services;
using BackupZCrypt.Desktop.Services.Interfaces;
using BackupZCrypt.Desktop.ViewModels;
using BackupZCrypt.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;

namespace BackupZCrypt.Desktop;

public sealed class App : Avalonia.Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = ConfigureServices();
            desktop.Exit += (_, _) => services.Dispose();

            ApplyLanguagePreference(services);

            desktop.MainWindow = new MainWindow
            {
                DataContext = services.GetRequiredService<MainWindowViewModel>(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static ServiceProvider ConfigureServices()
    {
        ServiceCollection services = [];

        services.AddDomainServices();
        services.AddApplicationServices();

        services.AddSingleton<IFilePickerService, FilePickerService>();
        services.AddSingleton<IClipboardService, ClipboardService>();

        services.AddSingleton<CreateBackupViewModel>();
        services.AddSingleton<UpdateBackupViewModel>();
        services.AddSingleton<RestoreBackupViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<AboutViewModel>();
        services.AddSingleton<MainWindowViewModel>();

        return services.BuildServiceProvider();
    }

    private static void ApplyLanguagePreference(ServiceProvider services)
    {
        try
        {
            var settingsService = services.GetRequiredService<ISettingsService>();

            // The load runs on a thread-pool thread: blocking on the async call
            // directly would deadlock, because its continuations would queue onto
            // the Avalonia dispatcher that this method is blocking.
            var language = Task.Run(() => settingsService.GetOrCreateAsync<LanguageSettings>())
                .GetAwaiter()
                .GetResult();

            if (!string.IsNullOrWhiteSpace(language.LanguageCode))
            {
                CultureInfo culture = new(language.LanguageCode);
                CultureInfo.CurrentUICulture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Fall back to the system culture when preferences cannot be read.
        }
    }
}
