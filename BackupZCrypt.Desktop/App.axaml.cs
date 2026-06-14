using System.Globalization;

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

/// <summary>
/// The Avalonia application root: loads XAML, builds the dependency-injection container, applies the
/// language preference and shows the main window.
/// </summary>
public sealed class App : Avalonia.Application
{
    /// <summary>
    /// Loads the application's XAML resources.
    /// </summary>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Completes framework initialization by wiring up services, applying the saved language and
    /// creating the main window for the classic desktop lifetime.
    /// </summary>
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

        _ = services.AddDomainServices();
        _ = services.AddApplicationServices();

        _ = services.AddSingleton<IFilePickerService, FilePickerService>();
        _ = services.AddSingleton<IClipboardService, ClipboardService>();

        _ = services.AddSingleton<CreateBackupViewModel>();
        _ = services.AddSingleton<UpdateBackupViewModel>();
        _ = services.AddSingleton<RestoreBackupViewModel>();
        _ = services.AddSingleton<SettingsViewModel>();
        _ = services.AddSingleton<AboutViewModel>();
        _ = services.AddSingleton<MainWindowViewModel>();

        return services.BuildServiceProvider();
    }

    private static void ApplyLanguagePreference(ServiceProvider services)
    {
        try
        {
            var settingsService = services.GetRequiredService<ISettingsService>();

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
        }
    }
}
